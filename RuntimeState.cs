namespace PenPulse;

internal sealed class PauseState
{
    public bool IsPaused
    {
        get; private set;
    }
    public long? Until
    {
        get; private set;
    }

    public void Set(bool paused)
    {
        IsPaused = paused;
        Until = null;
    }

    public void ForMinutes(long now, int minutes)
    {
        IsPaused = true;
        Until = now + Math.Clamp(minutes, 1, 1440) * 60000L;
    }

    public bool Tick(long now)
    {
        if (!IsPaused || Until == null || now < Until.Value)
            return false;
        Set(false);
        return true;
    }
}

internal sealed class ReconnectSchedule
{
    private static readonly int[] Delays = [500, 1000, 2000, 5000, 10000, 30000];
    private long nextAt;
    private long lastAt = long.MinValue / 2;
    public int Attempts
    {
        get; private set;
    }

    public void Reset(long now)
    {
        Attempts = 0;
        nextAt = now;
        lastAt = long.MinValue / 2;
    }

    public bool TryTake(long now, bool freshInput)
    {
        if (now - lastAt < 500 || (!freshInput && now < nextAt))
            return false;
        lastAt = now;
        nextAt = now + Delays[Math.Min(Attempts, Delays.Length - 1)];
        Attempts++;
        return true;
    }
}

internal static class PressureResponse
{
    public static double? Normalize(uint raw, int minimum, int maximum, ushort bits)
    {
        if (maximum <= minimum || bits is 0 or > 32)
            return null;
        long value = raw;
        if (minimum < 0)
        {
            if (bits == 32)
                value = unchecked((int)raw);
            else if ((raw & (1u << (bits - 1))) != 0)
                value -= 1L << bits;
        }

        if (value < minimum || value > maximum)
            return null;
        return (value - minimum) / (double)((long)maximum - minimum);
    }

    public static double Multiplier(double? pressure, bool enabled, double sensitivity, double floor)
    {
        if (!enabled || pressure == null || !double.IsFinite(pressure.Value))
            return 1;
        sensitivity = double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, .25, 3) : 1;
        floor = double.IsFinite(floor) ? Math.Clamp(floor, 0, 1) : .15;
        return floor + (1 - floor) * Math.Pow(Math.Clamp(pressure.Value, 0, 1), 1 / sensitivity);
    }
}
