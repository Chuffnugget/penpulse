namespace PenPulse;
// Microsoft defines the Bluetooth top button as Win+F20 / F19 / F18.
// Track only these keys and modifiers, separately for each Raw Input device.
// Ordinary keys are discarded and no keyboard text or key history is stored.
internal sealed class PenShortcutRecognizer
{
    // A release-only shortcut must follow this device's Windows-key event closely.
    // Expire abandoned chords separately so disconnects cannot leave modifiers held.
    private const int ReleaseFallbackMilliseconds = 250;
    private const int DeviceStateExpiryMilliseconds = 2000;
    private sealed class State
    {
        public readonly HashSet<ushort> Down = new();
        public long LastAt;
        public long LastWindowsAt = -10000, LastExtraModifierAt = -10000;
        public bool ChordEmitted, ChordBlocked;
    }

    private readonly Dictionary<nint, State> devices = new();
    public static bool IsRelevant(ushort key) => key is 0x81 or 0x82 or 0x83 or 0x5b or 0x5c or 0x10 or 0x11 or 0x12 or >= 0xa0 and <= 0xa5;
    public Gesture? Feed(nint device, ushort key, ushort flags, ushort scanCode, long now, bool allowPenReleaseFallback = false)
    {
        if (device == 0 || !IsRelevant(key) || scanCode == 0xff)
            return null;
        // Raw keyboard modifiers may use the generic VK rather than L/R VK.
        key = key switch
        {
            0x10 => (ushort)(scanCode == 0x36 ? 0xa1 : 0xa0),
            0x11 => (ushort)((flags & 2) != 0 ? 0xa3 : 0xa2),
            0x12 => (ushort)((flags & 2) != 0 ? 0xa5 : 0xa4),
            _ => key
        };
        if (!devices.TryGetValue(device, out var state) || now - state.LastAt > DeviceStateExpiryMilliseconds)
        {
            if (devices.Count >= 64)
                devices.Clear();
            devices[device] = state = new State();
        }

        state.LastAt = now;
        if ((flags & 1) != 0)
        {
            bool hadPress = state.Down.Remove(key);
            if (key is >= 0xa0 and <= 0xa5)
            {
                state.LastExtraModifierAt = now;
                state.ChordBlocked = true;
            }

            // On this Slim Pen, Windows delivers Win-down, Win-up, F20-up;
            // the shortcut key's down event is absent after the shell handles it.
            // Only accept that orphan release from an identified pen keyboard,
            // immediately following its own Win event, once per shortcut chord.
            if (allowPenReleaseFallback && key is >= 0x81 and <= 0x83 && !hadPress && !state.ChordEmitted && !state.ChordBlocked && now - state.LastWindowsAt <= ReleaseFallbackMilliseconds && now - state.LastExtraModifierAt > ReleaseFallbackMilliseconds)
            {
                state.ChordEmitted = true;
                return ActionFor(key);
            }

            return null;
        }

        if (!state.Down.Add(key))
            return null; // Ignore auto-repeat until release.
        if (key is 0x5b or 0x5c)
        {
            state.LastWindowsAt = now;
            state.ChordEmitted = false;
            state.ChordBlocked = state.Down.Any(k => k is >= 0xa0 and <= 0xa5);
            return null;
        }

        if (key is >= 0xa0 and <= 0xa5)
        {
            state.LastExtraModifierAt = now;
            state.ChordBlocked = true;
            return null;
        }

        if (key is < 0x81 or > 0x83 || !(state.Down.Contains(0x5b) || state.Down.Contains(0x5c)))
            return null;
        // Win+Ctrl+F19/F20 means dock/undock, not a top-button click.
        if (state.Down.Any(k => k is >= 0xa0 and <= 0xa5))
            return null;
        state.ChordEmitted = true;
        return ActionFor(key);
    }

    private static Gesture ActionFor(ushort key) => key switch
    {
        0x83 => Gesture.TopClick,
        0x82 => Gesture.TopDoubleClick,
        _ => Gesture.TopHold
    };
    public void DeviceRemoved(nint device) => devices.Remove(device);
    public void Reset() => devices.Clear();
    public static bool IsShortcut(Gesture gesture) => gesture is Gesture.TopClick or Gesture.TopDoubleClick or Gesture.TopHold;
}

internal static class PenKeyboardIdentity
{
    // Bluetooth HID service + the Slim Pen 2 VID/PID observed on this device.
    // Do not identify arbitrary keyboards from their friendly names.
    public static bool SupportsReleaseFallback(string path) => path.StartsWith(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02045e_PID&0c0f_", StringComparison.OrdinalIgnoreCase) && path.Contains("&Col01#", StringComparison.OrdinalIgnoreCase);
}
