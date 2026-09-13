using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class WorkIntervalTests
{
    static readonly TimeSpan Offset = TimeSpan.FromHours(10);
    static readonly DerivationOptions Defaults = new();

    static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 17, hour, minute, 0, Offset);

    static Observation Seen(ObservedEvent observed, DateTimeOffset at) =>
        new() { Timestamp = at, UnbiasedMs = 0, Event = observed };

    static IReadOnlyList<Interval> Active(params Observation[] observations) =>
        WorkIntervals.Build(observations, Defaults).Active;

    [Fact]
    public void Build_InactivityInsideDisplayOn_EndsIntervalAtTheInactivity()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.UserInactive, At(12)),
            Seen(ObservedEvent.UserPresent, At(13)),
            Seen(ObservedEvent.DisplayOff, At(17)));

        Assert.Equal(
            [new Interval(At(9), At(12)), new Interval(At(13), At(17))],
            active);
    }

    [Fact]
    public void Build_DisplayOnConfirmedByLaterUnlock_BillsFromDisplayOn()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(8)),
            Seen(ObservedEvent.Unlock, At(8, 10)),
            Seen(ObservedEvent.DisplayOff, At(17)));

        Assert.Equal([new Interval(At(8), At(17))], active);
    }

    [Fact]
    public void Build_DisplayOnNeverConfirmed_IsDiscardedAsSpuriousWake()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(3)),
            Seen(ObservedEvent.DisplayOff, At(3, 5)));

        Assert.Empty(active);
    }

    [Fact]
    public void Build_BriefInactivity_IsBridgedBackIntoTheBlock()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.UserInactive, At(10)),
            Seen(ObservedEvent.UserPresent, At(10, 5)),
            Seen(ObservedEvent.DisplayOff, At(17)));

        Assert.Equal([new Interval(At(9), At(17))], active);
    }

    [Fact]
    public void Build_BriefLock_IsNotBridged()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.Lock, At(10)),
            Seen(ObservedEvent.Unlock, At(10, 5)),
            Seen(ObservedEvent.DisplayOff, At(17)));

        Assert.Equal(
            [new Interval(At(9), At(10)), new Interval(At(10, 5), At(17))],
            active);
    }

    [Fact]
    public void Build_SuspendWithoutDisplayOff_EndsTheInterval()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.Suspend, At(16)),
            Seen(ObservedEvent.Resume, At(20)),
            Seen(ObservedEvent.UserPresent, At(20)),
            Seen(ObservedEvent.DisplayOff, At(21)));

        Assert.Equal(
            [new Interval(At(9), At(16)), new Interval(At(20), At(21))],
            active);
    }

    [Fact]
    public void Build_TickGapBeyondTolerance_IsReportedAsRecorderGap()
    {
        var active = WorkIntervals.Build(
        [
            Seen(ObservedEvent.Tick, At(9)),
            Seen(ObservedEvent.Tick, At(9, 1)),
            Seen(ObservedEvent.Tick, At(11)),
        ], Defaults);

        var gap = Assert.Single(active.Gaps);
        Assert.Equal(new Interval(At(9, 1), At(11)), gap);
    }

    [Fact]
    public void Build_BridgeAcrossLockEnabled_MergesOverTheLock()
    {
        var timeline = WorkIntervals.Build(
        [
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.Lock, At(10)),
            Seen(ObservedEvent.Unlock, At(10, 5)),
            Seen(ObservedEvent.DisplayOff, At(17)),
        ], Defaults with { BridgeAcrossLock = true });

        Assert.Equal([new Interval(At(9), At(17))], timeline.Active);
    }
}
