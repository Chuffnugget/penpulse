namespace PenPulse;
// Tracks actual motion during contact. A short expiry stops texture even when a
// final pen-up report is lost. Small stationary coordinate jitter is ignored.
internal sealed class StrokeMotion
{
    public double MovementThreshold { get; set; } = 2;
    public int IdleMilliseconds { get; set; } = 140;

    private bool contact, moved;
    private double anchorX, anchorY;
    private long lastMovement, lastSample;
    public void Feed(PenSample sample, long now)
    {
        if (!sample.Contact || !sample.InRange)
        {
            Reset();
            return;
        }

        lastSample = now;
        if (!contact)
        {
            contact = true;
            moved = false;
            anchorX = sample.X;
            anchorY = sample.Y;
            return;
        }

        var dx = sample.X - anchorX;
        var dy = sample.Y - anchorY;
        if (dx * dx + dy * dy >= MovementThreshold * MovementThreshold)
        {
            anchorX = sample.X;
            anchorY = sample.Y;
            lastMovement = now;
            moved = true;
        }
    }

    public bool IsMoving(long now) => contact && moved && now - lastMovement >= 0 && now - lastMovement < IdleMilliseconds;
    public bool IsContactRecent(long now) => contact && now - lastSample >= 0 && now - lastSample < 400;
    public void Reset()
    {
        contact = moved = false;
        lastMovement = 0;
    }
}
