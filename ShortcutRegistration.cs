using System.Runtime.InteropServices;

namespace PenPulse;

internal sealed class ShortcutRegistration(nint window) : IDisposable
{
    public const int Message = 0x0312;
    public const int Id = 0x5050;
    private (bool Enabled, uint Modifiers, uint Key)? current;
    private bool registered;
    public string Status { get; private set; } = "Pause shortcut is off.";

    public void Apply(DesktopOptions options)
    {
        options.Normalize();
        var next = (options.PauseShortcutEnabled, options.PauseShortcutModifiers, options.PauseShortcutKey);
        if (current == next)
            return;
        if (registered)
            UnregisterHotKey(window, Id);
        registered = false;
        current = next;
        if (!options.PauseShortcutEnabled)
        {
            Status = "Pause shortcut is off.";
            return;
        }

        registered = RegisterHotKey(window, Id, options.PauseShortcutModifiers | 0x4000, options.PauseShortcutKey);
        Status = registered ? "Pause shortcut ready." : "Shortcut unavailable (another app may use it). Choose a different combination.";
    }

    public void Dispose()
    {
        if (registered)
            UnregisterHotKey(window, Id);
        registered = false;
    }

    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
