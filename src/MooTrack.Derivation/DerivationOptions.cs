namespace MooTrack.Derivation;

public sealed record DerivationOptions
{
    public TimeSpan BridgeThreshold { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ConfirmationWindow { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan FringeMaxDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan FringeGap { get; init; } = TimeSpan.FromMinutes(90);
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan GapTolerance { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a recorder outage may be and still count as working time, when there
    /// is work either side of it. A machine restarting while you sit waiting for it is
    /// work; a night, a weekend or leave is not, and absence alone cannot tell them
    /// apart. Beyond this the time is excluded and the day flagged.
    /// </summary>
    public TimeSpan RestartAllowance { get; init; } = TimeSpan.FromMinutes(30);
    public bool BridgeAcrossLock { get; init; }
}
