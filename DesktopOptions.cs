namespace PenPulse;

internal sealed class DesktopOptions
{
    public bool AutoReconnect { get; set; } = true;
    public bool PauseShortcutEnabled
    {
        get; set;
    }
    public uint PauseShortcutModifiers { get; set; } = 3; // Ctrl + Alt.
    public uint PauseShortcutKey { get; set; } = 0x50; // P.

    public void Normalize()
    {
        if (PauseShortcutModifiers is not (3 or 5 or 6 or 7))
            PauseShortcutModifiers = 3;
        // Letters, digits and F1–F11 only; avoid OS-reserved combinations and F12.
        if (!(PauseShortcutKey is >= 0x41 and <= 0x5a or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7a))
            PauseShortcutKey = 0x50;
    }
}
