using Windows.Devices.Haptics;
using Windows.Devices.Input;
using Windows.Foundation.Metadata;

namespace PenPulse;

internal sealed class Haptics : IDisposable
{
    // Scheduling bounds only: the device owns the actual Click waveform duration.
    // The small grace period avoids immediately overlapping a just-sent effect.
    private const int CompletionGraceMilliseconds = 10;
    private const int MinimumCompletionMilliseconds = 30;
    private const int MaximumCompletionMilliseconds = 65;
    private SimpleHapticsController? controller;
    private SimpleHapticsControllerFeedback? click;
    private readonly DesktopHaptics desktop = new();
    private readonly System.Windows.Forms.Timer timer = new()
    {
        Interval = 15
    };
    private readonly Queue<(int Delay, double Strength)> pulses = new();
    private long stopAt, nextPulseAt;
    private bool playing, shortcutPattern;
    private bool drawingPlaying, lastSendRouted;
    private readonly DrawingPulseClock drawingClock = new();
    private Settings preferences = new();
    public bool Connected => controller != null && click != null;
    public double Intensity { get; set; } = 0.7;
    public long Commands
    {
        get; private set;
    }
    public long ConnectionAttempts
    {
        get; private set;
    }
    public string ConnectionStage { get; private set; } = "Waiting for a pen event";
    public string DesktopDiagnostic => desktop.Diagnostic;
    public string RoutingDiagnostic => desktop.SendDiagnostic;
    public long RoutedCommands => desktop.RoutedCommands;
    public long RejectedCommands => desktop.RejectedCommands;
    public long DrawingStarts
    {
        get; private set;
    }
    public long DrawingPulses
    {
        get; private set;
    }
    public bool DrawingActive => drawingClock.IsRequested(Environment.TickCount64);
    public bool DrawingAvailable => Connected;
    public long StopCommands => desktop.StopCommands;
    public string StopDiagnostic => desktop.StopDiagnostic;
    public string ConnectionMethod { get; private set; } = "None";
    public string SupportedWaveforms { get; private set; } = "";
    public string State { get; private set; } = "Touch the blue pad with your pen to connect.";

    public event Action? Changed;
    public Func<bool> PlaybackAllowed { get; set; } = () => true;

    public Haptics() => timer.Tick += (_, _) => Tick();
    public void Configure(Settings settings)
    {
        Cancel();
        preferences = settings;
        Intensity = settings.Intensity;
        drawingClock.IntervalMilliseconds = settings.DrawingIntervalMilliseconds;
    }

    public bool Connect(uint pointerId)
    {
        if (Connected)
            return true;
        try
        {
            ConnectionAttempts++;
            if (!ApiInformation.IsPropertyPresent("Windows.Devices.Input.PenDevice", "SimpleHapticsController"))
                throw new InvalidOperationException("This Windows version does not expose pen haptics.");
            var pen = pointerId == 0 ? null : PenDevice.GetFromPointerId(pointerId);
            var candidate = pen?.SimpleHapticsController;
            ConnectionStage = pen == null ? "PenDevice lookup returned null" : candidate == null ? "PenDevice has no haptics controller" : "PenDevice controller found";
            ConnectionMethod = candidate != null ? "PenDevice" : "None";
            if (candidate == null)
            {
                candidate = desktop.GetPenController();
                if (candidate != null)
                    ConnectionMethod = "InputHapticsManager";
            }

            SupportedWaveforms = candidate == null ? "" : string.Join(",", candidate.SupportedFeedback.Select(f => f.Waveform));
            var feedback = candidate?.SupportedFeedback.FirstOrDefault(f => f.Waveform == KnownSimpleHapticsControllerWaveforms.Click);
            if (candidate == null)
            {
                State = "Pen input detected, but no vibration controller is available yet. Touch here again after connecting the pen.";
                Changed?.Invoke();
                return false;
            }

            if (feedback == null)
            {
                ConnectionStage = "Controller found but Click waveform is missing";
                State = "Pen controller found, but it has no Click effect. Use Reconnect after checking tactile signals.";
                Changed?.Invoke();
                return false;
            }

            controller = candidate;
            click = feedback;
            ConnectionStage = "Connected";
            State = "Pen connected. Your changes apply immediately.";
            Changed?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            ConnectionStage = $"0x{e.HResult:X8}: {e.Message}";
            State = e.Message;
            Changed?.Invoke();
            return false;
        }
    }

    // Discrete gestures take priority. Drawing resumes after the sequence only
    // while recent contact movement is still requesting it.
    public void Play(Gesture gesture)
    {
        if (!Connected || Intensity <= 0)
            return;
        var effect = preferences.For(gesture);
        if (!effect.Enabled || effect.Strength <= 0)
            return;
        pulses.Clear();
        try
        {
            StopPlayback();
        }
        catch
        {
        }

        shortcutPattern = PenShortcutRecognizer.IsShortcut(gesture);
        var now = Environment.TickCount64;
        var pattern = effect.Pattern();
        int previousOffset = 0;
        foreach (var pulse in pattern)
        {
            pulses.Enqueue((pulse.Offset - previousOffset, pulse.Strength));
            previousOffset = pulse.Offset;
        }

        nextPulseAt = now;
        timer.Start();
        Tick();
    }

    public void SetDrawing(bool active)
    {
        active = active && Connected && Intensity > 0 && preferences.DrawingStrength > 0;
        var now = Environment.TickCount64;
        bool previouslyActive = drawingClock.IsRequested(now);
        drawingClock.Request(active, now);
        if (previouslyActive == active)
            return;
        if (active)
            DrawingStarts++;
        timer.Start();
        Tick();
    }

    private void Send(SimpleHapticsControllerFeedback feedback, double strength)
    {
        if (controller == null)
            return;
        lastSendRouted = desktop.TrySend(controller, feedback.Waveform, Math.Clamp(strength, 0, 1));
        if (!lastSendRouted)
        {
            if (controller.IsIntensitySupported)
                controller.SendHapticFeedback(feedback, Math.Clamp(strength, 0, 1));
            else
                controller.SendHapticFeedback(feedback);
        }

        Commands++;
    }

    private void StopPlayback()
    {
        if (!playing && !drawingPlaying)
            return;
        playing = drawingPlaying = false;
        if (!lastSendRouted || !desktop.TryStop())
            controller?.StopFeedback();
    }

    private void Tick()
    {
        var now = Environment.TickCount64;
        try
        {
            // Recheck before every send, including queued effects and drawing.
            // Cancelling discards the sequence so leaving an excluded app cannot replay it.
            if (!PlaybackAllowed())
            {
                Cancel();
                return;
            }

            bool drawingRequested = drawingClock.IsRequested(now);
            if (drawingPlaying && !drawingRequested)
                StopPlayback();
            // Click is finite. Let it finish naturally rather than sending a
            // stop that can overtake a delayed Bluetooth effect. The deadline
            // releases scheduling state only; cancellation still sends a stop.
            if ((playing || drawingPlaying) && now >= stopAt)
                playing = drawingPlaying = false;
            if (pulses.TryPeek(out var pulse) && nextPulseAt <= now)
            {
                pulses.Dequeue();
                if (controller == null || click == null)
                {
                    Cancel();
                    return;
                }

                StopPlayback();
                Send(click, Intensity * pulse.Strength);
                playing = true;
                // Preserve spacing after a late timer or a slow device call.
                // Never catch up overdue pulses on consecutive timer ticks.
                var sentAt = Environment.TickCount64;
                if (pulses.TryPeek(out var next))
                    nextPulseAt = sentAt + next.Delay;
                // Logical pulse end; the finite waveform finishes itself.
                stopAt = sentAt + Math.Clamp((int)click.Duration.TotalMilliseconds + CompletionGraceMilliseconds, MinimumCompletionMilliseconds, MaximumCompletionMilliseconds);
            }

            if (pulses.Count == 0 && !playing && click != null && drawingClock.TryTakePulse(now))
            {
                Send(click, Intensity * preferences.DrawingStrength);
                drawingPlaying = true;
                DrawingPulses++;
                stopAt = now + Math.Clamp((int)click.Duration.TotalMilliseconds + CompletionGraceMilliseconds, MinimumCompletionMilliseconds, MaximumCompletionMilliseconds);
            }

            if (pulses.Count == 0 && !playing && !drawingPlaying && !drawingRequested)
                timer.Stop();
        }
        catch (Exception e)
        {
            pulses.Clear();
            timer.Stop();
            playing = drawingPlaying = false;
            drawingClock.Reset();
            desktop.TryStop();
            try
            {
                controller?.StopFeedback();
            }
            catch
            {
            }

            controller = null;
            click = null;
            State = "Pen connection lost: " + e.Message;
            Changed?.Invoke();
        }
    }

    public void Cancel()
    {
        shortcutPattern = false;
        drawingClock.Reset();
        pulses.Clear();
        timer.Stop();
        try
        {
            StopPlayback();
        }
        catch
        {
        }
    }

    public void CancelContact()
    {
        // Leaving proximity ends contact feedback, but must not cancel a
        // Bluetooth top-button sequence pressed while withdrawing the pen.
        if (!shortcutPattern)
        {
            Cancel();
            return;
        }

        drawingClock.Reset();
        if (drawingPlaying)
        {
            try
            {
                StopPlayback();
            }
            catch
            {
            }
        }
    }

    public void Disconnect()
    {
        Cancel();
        controller = null;
        click = null;
        State = "Touch the blue pad with your pen to reconnect.";
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Cancel();
        timer.Dispose();
        desktop.Dispose();
    }
}
