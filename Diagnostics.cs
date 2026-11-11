using System.Text;

namespace PenPulse;

internal sealed record DiagnosticSnapshot(string Version, bool Connected, bool Paused, bool SessionSuspended, bool Excluded, bool InputFault, double OverallStrength, bool DrawingEnabled, double DrawingStrength, string ActiveProfile, long RawReports, long InputAgeMilliseconds, bool PressureSeen, bool IntensitySupported, string Texture, string Reconnect, string Shortcut, string ConnectionMethod, string Routing, string SaveError)
{
    public string Explanation => SessionSuspended ? "Feedback is suspended while Windows is locked or sleeping." : Paused ? "Feedback is paused. Choose Resume or use your pause shortcut." : Excluded ? "The foreground app is excluded, or its identity could not be read." : InputFault ? "The pen listener reported a problem. Reconnect, then review the connection details." : !Connected ? "No haptic controller is connected. Wake the pen and touch the blue pad; check Windows tactile signals if needed." : OverallStrength <= 0 ? "Overall strength is zero. Increase it in General." : "PenPulse is ready. A successful Windows request cannot confirm physical vibration.";

    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"PenPulse {Version} — troubleshooting");
        text.AppendLine(Explanation);
        text.AppendLine($"Connected: {Connected}; paused: {Paused}; excluded/unknown app: {Excluded}");
        text.AppendLine($"Active profile: {ActiveProfile}");
        text.AppendLine($"Overall strength: {OverallStrength:P0}; drawing enabled: {DrawingEnabled}; drawing strength: {DrawingStrength:P0}");
        text.AppendLine($"Raw pen reports: {RawReports}; last report: {(InputAgeMilliseconds < 0 ? "none" : InputAgeMilliseconds + " ms ago")}");
        text.AppendLine($"Pressure received: {PressureSeen}; intensity supported: {IntensitySupported}");
        text.AppendLine($"Texture: {Texture}");
        text.AppendLine($"Connection: {ConnectionMethod}");
        text.AppendLine($"Reconnect: {Reconnect}");
        text.AppendLine($"Pause shortcut: {Shortcut}");
        text.AppendLine($"Routing: {Routing}");
        if (!string.IsNullOrEmpty(SaveError))
            text.AppendLine("A local settings file could not be loaded or saved. See the app for details.");
        text.AppendLine("This report contains counts and current settings, not handwriting, keystrokes or screen contents. No automatic upload.");
        return text.ToString();
    }
}
