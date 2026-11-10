namespace PenPulse;
// Raw HID reports have a short lease. Mouse promotion is event-driven, so a
// stationary fallback contact is verified against the current button state.
internal sealed class PenContactState(Func<bool> fallbackButtonDown)
{
    // Allow brief report gaps, but release contact before a lost pen-up can keep
    // a hold or drawing effect alive indefinitely. This is an app tolerance.
    public const int RawExpiryMilliseconds = 400;
    public GestureRecognizer Gestures { get; } = new();
    public StrokeMotion Stroke { get; } = new();

    public event Action? Expired;
    private bool rawContact;
    private long lastSampleAt;
    private PenSample lastSample;
    public void Feed(PenSample sample, long now, bool raw, bool newContact = false)
    {
        // Ordered mouse up events must still finish a drag even if Windows'
        // current button state is already up when the queued event is handled.
        if (rawContact)
            Expire(now);
        if (newContact && Gestures.Contact)
        {
            Reset();
            Expired?.Invoke();
        }

        rawContact = raw;
        lastSampleAt = now;
        lastSample = sample;
        Stroke.Feed(sample, now);
        Gestures.Feed(sample, now);
    }

    public void Tick(long now)
    {
        if (Expire(now))
            return;
        if (Gestures.Contact && !rawContact)
            Stroke.Feed(lastSample, now);
        Gestures.Tick(now);
    }

    private bool Expire(long now)
    {
        if (!Gestures.Contact)
            return false;
        bool alive = rawContact ? now >= lastSampleAt && now - lastSampleAt < RawExpiryMilliseconds : fallbackButtonDown();
        if (alive)
            return false;
        Reset();
        Expired?.Invoke();
        return true;
    }

    public void Reset()
    {
        Gestures.Reset();
        Stroke.Reset();
        rawContact = false;
        lastSampleAt = 0;
        lastSample = default;
    }
}
