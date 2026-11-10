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
        public static void Reset()
        {
            Sends.Clear();
            Stops = 0;
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
    }

    public sealed class SimpleHapticsControllerFeedback
    {
        public ushort Waveform => 4099;
        public TimeSpan Duration => TimeSpan.FromMilliseconds(20);
    }

    public sealed class SimpleHapticsController
    {
        public IReadOnlyList<SimpleHapticsControllerFeedback> SupportedFeedback { get; } = [new()];
        public bool IsIntensitySupported => true;

        public void SendHapticFeedback(SimpleHapticsControllerFeedback feedback, double strength = 1) => PenPulse.Testing.Hardware.Sends.Add((PenPulse.Testing.Clock.TickCount64, strength));
        public void StopFeedback() => PenPulse.Testing.Hardware.Stops++;
    }
}

namespace Windows.Devices.Input
{
    public sealed class PenDevice
    {
        public static PenDevice GetFromPointerId(uint id) => new();
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

        public Windows.Devices.Haptics.SimpleHapticsController GetPenController() => new();
        public bool TrySend(Windows.Devices.Haptics.SimpleHapticsController controller, ushort waveform, double intensity)
        {
            PenPulse.Testing.Hardware.Sends.Add((PenPulse.Testing.Clock.TickCount64, intensity));
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
