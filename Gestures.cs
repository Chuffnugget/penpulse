namespace PenPulse;

internal enum Gesture
{
    Press,
    Hold,
    RightClick,
    DoubleTap,
    Drag,
    Release,
    Eraser,
    TopClick,
    TopDoubleClick,
    TopHold
}

internal readonly record struct PenSample(bool Contact, bool Barrel, bool Eraser, bool InRange, double X, double Y)
{
    // HID Eraser (0x45) is contact at the tail end; Invert (0x3c) can also
    // be set during hover. Erasing need not set the writing Tip Switch (0x42).
    public static PenSample FromSwitches(bool tip, bool eraser, bool inverted, bool barrel, bool inRange, double x, double y) => new(tip || eraser, barrel, eraser || inverted, inRange, x, y);
}

// Pure state machine: time and coordinates come from the input adapter, not a global clock.
internal sealed class GestureRecognizer
{
    public int HoldMilliseconds { get; set; } = 550;
    public int DoubleTapMilliseconds { get; set; } = 450;
    public double MovementThreshold { get; set; } = 14;
    public bool Contact
    {
        get; private set;
    }

    public event Action<Gesture>? Recognized;
    private bool held, moved, right, eraser, completedPair;
    private double startX, startY, lastTapX, lastTapY;
    private long started, lastTap = -10000, suppressRightUntil;
    public void Feed(PenSample s, long now)
    {
        if (!s.InRange)
        {
            Reset();
            return;
        }

        if (s.Contact && !Contact)
        {
            Contact = true;
            held = moved = false;
            right = s.Barrel;
            eraser = s.Eraser;
            started = now;
            startX = s.X;
            startY = s.Y;
            var twice = now - lastTap <= DoubleTapMilliseconds && Distance(s.X, s.Y, lastTapX, lastTapY) <= MovementThreshold * 2;
            completedPair = twice;
            Recognized?.Invoke(eraser ? Gesture.Eraser : right ? Gesture.RightClick : twice ? Gesture.DoubleTap : Gesture.Press);
            if (right)
                suppressRightUntil = now + 1200;
            if (twice || right || eraser)
                lastTap = -10000;
        }

        if (Contact && s.Contact)
        {
            if (!moved && Distance(s.X, s.Y, startX, startY) > MovementThreshold)
            {
                moved = true;
                if (!right && !eraser)
                    Recognized?.Invoke(Gesture.Drag);
            }

            if (s.Barrel && !right && !held && !eraser)
            {
                right = true;
                suppressRightUntil = now + 1200;
                Recognized?.Invoke(Gesture.RightClick);
            }

            Tick(now);
        }

        if (Contact && !s.Contact)
        {
            if (!held && !moved && !right && !eraser && !completedPair)
            {
                lastTap = now;
                lastTapX = s.X;
                lastTapY = s.Y;
            }
            else
                lastTap = -10000;
            if (held || right)
                suppressRightUntil = now + 900;
            if (moved && !right && !eraser)
                Recognized?.Invoke(Gesture.Release);
            Contact = false;
        }
    }

    public void Tick(long now)
    {
        if (Contact && !held && !moved && !right && !eraser && now - started >= HoldMilliseconds)
        {
            held = true;
            suppressRightUntil = now + 1200;
            Recognized?.Invoke(Gesture.Hold);
        }
    }

    // A press-and-hold often causes a later promoted right mouse click. Do not buzz twice.
    public void PromotedRightClick(long now)
    {
        if (now < suppressRightUntil || (Contact && (right || held)))
            return;
        right = true;
        suppressRightUntil = now + 900;
        Recognized?.Invoke(Gesture.RightClick);
    }

    public void Reset()
    {
        Contact = held = moved = right = eraser = completedPair = false;
        lastTap = -10000;
        suppressRightUntil = 0;
    }

    private static double Distance(double x, double y, double x2, double y2) => Math.Sqrt((x - x2) * (x - x2) + (y - y2) * (y - y2));
}

internal static class PenSignature
{
    public static bool IsPen(ulong extraInfo) => ((uint)extraInfo & 0xffffff00) == 0xff515700 && (extraInfo & 0x80) == 0;
}
