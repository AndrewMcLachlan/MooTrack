namespace MooTrack.Derivation;

public sealed record DerivationOptions
{
    public TimeSpan BridgeThreshold { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ConfirmationWindow { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan FringeMaxDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan FringeGap { get; init; } = TimeSpan.FromMinutes(90);
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan GapTolerance { get; init; } = TimeSpan.FromMinutes(5);
    public bool BridgeAcrossLock { get; init; }
}
