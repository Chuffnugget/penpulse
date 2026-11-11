namespace PenPulse;

internal sealed class EffectSettings
{
    public bool Enabled { get; set; } = true;
    public double Strength { get; set; } = 1;
    public int PulseCount { get; set; } = 1;
    public int GapMilliseconds { get; set; } = 85;
    public bool AlternatePulses
    {
        get; set;
    }

    public void Normalize()
    {
        Strength = double.IsFinite(Strength) ? Math.Clamp(Strength, 0, 1) : 1;
        PulseCount = Math.Clamp(PulseCount, 1, 6);
        GapMilliseconds = Math.Clamp(GapMilliseconds, 50, 400);
    }

    public (int Offset, double Strength)[] Pattern()
    {
        Normalize();
        return Enumerable.Range(0, PulseCount).Select(i => (i * GapMilliseconds, Strength * (AlternatePulses && i % 2 == 0 ? .625 : 1))).ToArray();
    }
}
