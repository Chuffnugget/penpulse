namespace PenPulse;
// Reuses finite Click effects for movement feedback. Refreshing motion does not
// increase the pulse rate, and a delayed UI thread never replays missed pulses.
internal sealed class DrawingPulseClock
{
    private int intervalMilliseconds = 85;
    public int IntervalMilliseconds
    {
        get => intervalMilliseconds; set => intervalMilliseconds = Math.Clamp(value, 50, 500);
    }

    // Motion requests are refreshed by the input loop. Drop them after a gap so
    // a lost release cannot leave movement feedback running on its own.
    public const int LeaseMilliseconds = 200;
    private bool requested;
    private long expiresAt, nextPulseAt;
    public bool IsRequested(long now) => requested && now < expiresAt;
    public void Request(bool active, long now)
    {
        if (!active)
        {
            Reset();
            return;
        }

        if (!IsRequested(now))
            nextPulseAt = now;
        requested = true;
        expiresAt = now + LeaseMilliseconds;
    }

    public bool TryTakePulse(long now)
    {
        if (!IsRequested(now) || now < nextPulseAt)
            return false;
        nextPulseAt = now + IntervalMilliseconds;
        return true;
    }

    public void Reset()
    {
        requested = false;
        expiresAt = nextPulseAt = 0;
    }
}
