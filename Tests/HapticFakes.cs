// Test-only Windows adapters; the real Haptics scheduler is linked into this runner.
global using Environment = PenPulse.Testing.Clock;

namespace PenPulse.Testing
{
    public static class Clock
    {
        public static long TickCount64
        {
            get; set;
        }
        public static int ExitCode
        {
            get => System.Environment.ExitCode; set => System.Environment.ExitCode = value;
        }
    }

    public static class Hardware
    {
        public static readonly List<(long Time, double Strength)> Sends = new();
        public static int Stops;
        public static bool SupportsTextures, SupportsIntensity = true, SupportsDuration = true, RejectDuration, FailConnection;
        public static readonly List<(long Time, ushort Waveform, double Strength, int Duration)> Effects = new();
        public static void Reset()
        {
            Sends.Clear();
            Stops = 0;
            Effects.Clear();
            SupportsTextures = RejectDuration = FailConnection = false;
            SupportsIntensity = SupportsDuration = true;
        }
    }
}

namespace System.Windows.Forms
{
    public sealed class Timer : IDisposable
    {
        public static readonly List<Timer> Instances = new();
        public Timer()
        {
            Instances.Add(this);
        }

        public int Interval
        {
            get; set;
        }
        public bool Enabled
        {
            get; private set;
        }

        public event EventHandler? Tick;
        public void Start() => Enabled = true;
        public void Stop() => Enabled = false;
        public void Fire()
        {
            if (Enabled)
                Tick?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Stop();
            Instances.Remove(this);
        }
    }
}

namespace Windows.Foundation.Metadata
{
    public static class ApiInformation
    {
        public static bool IsPropertyPresent(string type, string property) => true;
    }
}

namespace Windows.Devices.Haptics
{
    public static class KnownSimpleHapticsControllerWaveforms
    {
        public const ushort Click = 4099;
        public const ushort InkContinuous = 4107, PencilContinuous = 4108, BrushContinuous = 4109, MarkerContinuous = 4110, EraserContinuous = 4112;
    }

    public sealed class SimpleHapticsControllerFeedback
    {
        public ushort Waveform { get; init; } = 4099;
        public TimeSpan Duration => TimeSpan.FromMilliseconds(20);
    }

    public sealed class SimpleHapticsController
    {
        public IReadOnlyList<SimpleHapticsControllerFeedback> SupportedFeedback => PenPulse.Testing.Hardware.SupportsTextures ? [new(), new()
        {
            Waveform = KnownSimpleHapticsControllerWaveforms.InkContinuous
        }

        ] : [new()];
        public bool IsIntensitySupported => PenPulse.Testing.Hardware.SupportsIntensity;
        public bool IsPlayDurationSupported => PenPulse.Testing.Hardware.SupportsDuration;

        public void SendHapticFeedbackForDuration(SimpleHapticsControllerFeedback feedback, double intensity, TimeSpan duration)
        {
            if (PenPulse.Testing.Hardware.RejectDuration)
                throw new InvalidOperationException("Duration rejected");
            PenPulse.Testing.Hardware.Sends.Add((PenPulse.Testing.Clock.TickCount64, intensity));
            PenPulse.Testing.Hardware.Effects.Add((PenPulse.Testing.Clock.TickCount64, feedback.Waveform, intensity, (int)duration.TotalMilliseconds));
        }

        public void SendHapticFeedback(SimpleHapticsControllerFeedback feedback, double strength = 1) => PenPulse.Testing.Hardware.Sends.Add((PenPulse.Testing.Clock.TickCount64, strength));
        public void StopFeedback() => PenPulse.Testing.Hardware.Stops++;
    }
}

namespace Windows.Devices.Input
{
    public sealed class PenDevice
    {
        public static PenDevice? GetFromPointerId(uint id) => PenPulse.Testing.Hardware.FailConnection ? null : new();
        public Windows.Devices.Haptics.SimpleHapticsController SimpleHapticsController { get; } = new();
    }
}

namespace PenPulse
{
    internal sealed class DesktopHaptics : IDisposable
    {
        public string Diagnostic => "fake";
        public string SendDiagnostic => "fake";
        public string StopDiagnostic => "fake";
        public long RoutedCommands
        {
            get; private set;
        }
        public long RejectedCommands => 0;
        public long StopCommands
        {
            get; private set;
        }

        public Windows.Devices.Haptics.SimpleHapticsController? GetPenController() => PenPulse.Testing.Hardware.FailConnection ? null : new();
        public bool TrySend(Windows.Devices.Haptics.SimpleHapticsController controller, ushort waveform, double intensity, int durationMilliseconds = 0)
        {
            if (durationMilliseconds > 0 && PenPulse.Testing.Hardware.RejectDuration)
                return false;
            PenPulse.Testing.Hardware.Sends.Add((PenPulse.Testing.Clock.TickCount64, intensity));
            PenPulse.Testing.Hardware.Effects.Add((PenPulse.Testing.Clock.TickCount64, waveform, intensity, durationMilliseconds));
            RoutedCommands++;
            return true;
        }

        public bool TryStop()
        {
            PenPulse.Testing.Hardware.Stops++;
            StopCommands++;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
