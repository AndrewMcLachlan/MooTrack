namespace MooTrack.Derivation;

public readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
}
