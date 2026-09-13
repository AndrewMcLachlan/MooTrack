namespace MooTrack.Derivation;

public enum DayQuality
{
    Complete,
    Partial,
    Unreliable,
}

public sealed record DayRecord
{
    public required DateOnly Date { get; init; }
    public required TimeOnly? FirstActive { get; init; }
    public required TimeOnly? LastActive { get; init; }
    public required decimal SpanHours { get; init; }
    public required decimal ActiveHours { get; init; }
    public required decimal AwayHours { get; init; }
    public required int Sessions { get; init; }
    public required int LongestBreakMinutes { get; init; }
    public required int FringeSessions { get; init; }
    public DayQuality Quality { get; init; } = DayQuality.Complete;
    public int UnaccountedMinutes { get; init; }
}
