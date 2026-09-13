namespace MooTrack.Derivation;

public enum ObservedEvent
{
    DisplayOn,
    DisplayOff,
    UserPresent,
    UserInactive,
    Lock,
    Unlock,
    Suspend,
    Resume,
    Shutdown,
    Tick,
    Gap,
}

public sealed record Observation
{
    public required DateTimeOffset Timestamp { get; init; }
    public required long UnbiasedMs { get; init; }
    public required ObservedEvent Event { get; init; }
    public string Host { get; init; } = "";
    public string User { get; init; } = "";
    public string Source { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed record ObservationLog(
    IReadOnlyList<Observation> Observations,
    IReadOnlyList<string> Malformed);
