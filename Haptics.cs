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
    private DesktopHaptics desktop = new();
    private SimpleHapticsControllerFeedback? texture;
    private SimpleHapticsControllerFeedback[] availableFeedback = [];
    private bool intensitySupported, durationSupported;
    private bool continuousPlaying;
    private long nextTextureAt;
    private double? drawingPressure;
    private const int TextureDurationMilliseconds = 120;
    private const int TextureRefreshMilliseconds = 80;
    public string TextureStatus { get; private set; } = "Pulse feedback";
    public bool IntensitySupported => Connected && intensitySupported;
    public IReadOnlyList<DrawingTexture> AvailableTextures => controller == null ? [DrawingTexture.Pulses] : Enum.GetValues<DrawingTexture>().Where(t => t == DrawingTexture.Pulses || availableFeedback.Any(f => f.Waveform == TextureWaveform(t))).ToArray();

    private readonly System.Windows.Forms.Timer timer = new()
    {
        Interval = 15
    };
    private readonly Queue<(int Delay, double Strength)> pulses = new();
    private long stopAt, nextPulseAt;
    private double sequenceIntensity;
    private bool playing, shortcutPattern;
    private bool drawingPlaying, lastSendRouted;
    private readonly DrawingPulseClock drawingClock = new();
    private Settings preferences = new();
    public bool Connected => controller != null && click != null;
    public double Intensity { get; set; } = 1;
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
    public void Configure(Settings settings, bool preserveShortcut = false)
    {
        if (preserveShortcut && shortcutPattern)
            CancelContact();
        else
            Cancel();
        preferences = settings;
        Intensity = settings.Intensity;
        drawingClock.IntervalMilliseconds = settings.DrawingIntervalMilliseconds;
        SelectTexture();
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

            var supported = candidate?.SupportedFeedback.ToArray() ?? [];
            SupportedWaveforms = string.Join(",", supported.Select(f => f.Waveform));
            var feedback = supported.FirstOrDefault(f => f.Waveform == KnownSimpleHapticsControllerWaveforms.Click);
            if (candidate == null)
            {
                State = "No vibration controller is available yet. Wake the pen or touch the blue pad.";
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

            // Cache capabilities while connecting. Reading them from a disconnected
            // COM device during a status refresh must not crash the window.
            bool supportsIntensity = candidate.IsIntensitySupported;
            bool supportsDuration = candidate.IsPlayDurationSupported;
            controller = candidate;
            click = feedback;
            availableFeedback = supported;
            intensitySupported = supportsIntensity;
            durationSupported = supportsDuration;
            SelectTexture();
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
        sequenceIntensity = Intensity;
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

    public void SetDrawing(bool active, double? pressure = null)
    {
        drawingPressure = pressure;
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
            if (intensitySupported)
                controller.SendHapticFeedback(feedback, Math.Clamp(strength, 0, 1));
            else
                controller.SendHapticFeedback(feedback);
        }

        Commands++;
    }

    private void StopPlayback()
    {
        if (!playing && !drawingPlaying && !continuousPlaying)
            return;
        playing = drawingPlaying = continuousPlaying = false;
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
            if ((drawingPlaying || continuousPlaying) && !drawingRequested)
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
                Send(click, sequenceIntensity * pulse.Strength);
                playing = true;
                // Preserve spacing after a late timer or a slow device call.
                // Never catch up overdue pulses on consecutive timer ticks.
                var sentAt = Environment.TickCount64;
                if (pulses.TryPeek(out var next))
                    nextPulseAt = sentAt + next.Delay;
                // Logical pulse end; the finite waveform finishes itself.
                stopAt = sentAt + Math.Clamp((int)click.Duration.TotalMilliseconds + CompletionGraceMilliseconds, MinimumCompletionMilliseconds, MaximumCompletionMilliseconds);
            }

            if (pulses.Count == 0 && !playing && drawingRequested && texture != null && (!continuousPlaying || now >= nextTextureAt))
            {
                if (TryTexture(texture, DrawingIntensity()))
                {
                    continuousPlaying = true;
                    nextTextureAt = Environment.TickCount64 + TextureRefreshMilliseconds;
                    DrawingPulses++;
                }
                else
                {
                    StopPlayback();
                    texture = null;
                    TextureStatus = "Texture playback was declined; using pulses until settings change or reconnect.";
                }
            }

            if (pulses.Count == 0 && !playing && texture == null && click != null && drawingClock.TryTakePulse(now))
            {
                Send(click, DrawingIntensity());
                drawingPlaying = true;
                DrawingPulses++;
                stopAt = now + Math.Clamp((int)click.Duration.TotalMilliseconds + CompletionGraceMilliseconds, MinimumCompletionMilliseconds, MaximumCompletionMilliseconds);
            }

            if (pulses.Count == 0 && !playing && !drawingPlaying && !continuousPlaying && !drawingRequested)
                timer.Stop();
        }
        catch (Exception e)
        {
            pulses.Clear();
            timer.Stop();
            playing = drawingPlaying = continuousPlaying = false;
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
            texture = null;
            availableFeedback = [];
            intensitySupported = durationSupported = false;
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
        if (drawingPlaying || continuousPlaying)
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
        texture = null;
        availableFeedback = [];
        intensitySupported = durationSupported = false;
        desktop.Dispose();
        desktop = new DesktopHaptics();
        State = "Waiting to reconnect. Use the pen, or touch the blue pad.";
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Cancel();
        timer.Dispose();
        desktop.Dispose();
    }

    private double DrawingIntensity() => Intensity * preferences.DrawingStrength * PressureResponse.Multiplier(drawingPressure, preferences.PressureFeedback && IntensitySupported, preferences.PressureSensitivity, preferences.MinimumPressureStrength);
    private static ushort TextureWaveform(DrawingTexture kind) => kind switch
    {
        DrawingTexture.Ink => KnownSimpleHapticsControllerWaveforms.InkContinuous,
        DrawingTexture.Pencil => KnownSimpleHapticsControllerWaveforms.PencilContinuous,
        DrawingTexture.Brush => KnownSimpleHapticsControllerWaveforms.BrushContinuous,
        DrawingTexture.Marker => KnownSimpleHapticsControllerWaveforms.MarkerContinuous,
        DrawingTexture.Eraser => KnownSimpleHapticsControllerWaveforms.EraserContinuous,
        _ => KnownSimpleHapticsControllerWaveforms.Click
    };
    private void SelectTexture()
    {
        nextTextureAt = 0;
        texture = preferences.DrawingTexture == DrawingTexture.Pulses ? null : availableFeedback.FirstOrDefault(f => f.Waveform == TextureWaveform(preferences.DrawingTexture));
        TextureStatus = preferences.DrawingTexture == DrawingTexture.Pulses ? "Pulse feedback" : texture != null ? preferences.DrawingTexture + " texture (experimental)" : "Requested texture unavailable; using pulses.";
    }

    private bool TryTexture(SimpleHapticsControllerFeedback feedback, double intensity)
    {
        if (controller == null)
            return false;
        // Never send an unbounded continuous waveform. Short requests expire even
        // if the app stalls; normal contact loss also sends an explicit stop.
        bool routed = desktop.TrySend(controller, feedback.Waveform, Math.Clamp(intensity, 0, 1), TextureDurationMilliseconds);
        if (!routed)
        {
            if (!durationSupported)
                return false;
            try
            {
                controller.SendHapticFeedbackForDuration(feedback, Math.Clamp(intensity, 0, 1), TimeSpan.FromMilliseconds(TextureDurationMilliseconds));
            }
            catch
            {
                return false;
            }
        }

        lastSendRouted = routed;
        Commands++;
        return true;
    }
}
