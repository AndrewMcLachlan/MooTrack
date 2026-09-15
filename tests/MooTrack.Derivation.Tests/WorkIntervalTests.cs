using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class WorkIntervalTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(10);
    private static readonly DerivationOptions Defaults = new();

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 17, hour, minute, 0, Offset);

    private static Observation Seen(ObservedEvent observed, DateTimeOffset at) =>
        new() { Timestamp = at, UnbiasedMs = 0, Event = observed };

    private static IReadOnlyList<Interval> Active(params Observation[] observations) =>
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

    [Fact]
    public void Build_AgentRestartWhileLocked_DoesNotEndTheBreakEarly()
    {
        var active = Active(
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            Seen(ObservedEvent.Lock, At(12)),
            Seen(ObservedEvent.AgentStopped, At(12, 30)),
            Seen(ObservedEvent.AgentStarted, At(12, 30)),
            Seen(ObservedEvent.Unlock, At(13)),
            Seen(ObservedEvent.DisplayOff, At(17)));

        Assert.Equal(
            [new Interval(At(9), At(12)), new Interval(At(13), At(17))],
            active);
    }

    private static WorkTimeline Timeline(params Observation[] observations) =>
        WorkIntervals.Build(observations, Defaults);

    private static Observation Tick(DateTimeOffset at) => Seen(ObservedEvent.Tick, at);

    private static IEnumerable<Observation> Ticks(DateTimeOffset from, DateTimeOffset to)
    {
        for (var at = from; at <= to; at = at.AddMinutes(1)) yield return Tick(at);
    }

    // The machine was off. It cannot be billed, whatever the display was doing before.
    [Fact]
    public void Build_TimeWithNoTicks_IsNotBilled()
    {
        Observation[] observations =
        [
            Seen(ObservedEvent.DisplayOn, At(9)),
            Seen(ObservedEvent.UserPresent, At(9)),
            .. Ticks(At(9), At(10)),
            .. Ticks(At(12), At(13)),
            Seen(ObservedEvent.DisplayOff, At(13)),
        ];

        var active = WorkIntervals.Build(observations, Defaults).Active;

        Assert.DoesNotContain(active, i => i.Start <= At(11) && i.End >= At(11));
    }

    // Thirty minutes of screen-on with the recorder ticking is use. A maintenance
    // wake does not hold the display on; discarding this loses real working time.
    [Fact]
    public void Build_SustainedScreenOn_ConfirmsTheReturnWithoutAPresenceEvent()
    {
        Observation[] observations =
        [
            Seen(ObservedEvent.DisplayOn, At(8)),
            .. Ticks(At(8), At(9)),
            Seen(ObservedEvent.DisplayOff, At(9)),
        ];

        var active = WorkIntervals.Build(observations, Defaults).Active;

        Assert.Equal([new Interval(At(8), At(9))], active);
    }

    // Sitting at the desk while the machine restarts is working time. A short outage
    // with work either side is a restart; a long one is a night, a weekend or leave,
    // and nothing distinguishes those from absence.
    [Fact]
    public void Build_ShortOutageBetweenWorkingBlocks_IsCountedAsWork()
    {
        Observation[] observations =
        [
            Seen(ObservedEvent.DisplayOn, At(8)),
            Seen(ObservedEvent.UserPresent, At(8)),
            .. Ticks(At(8), At(8, 30)),
            Seen(ObservedEvent.AgentStarted, At(8, 45)),
            Seen(ObservedEvent.DisplayOn, At(8, 45)),
            Seen(ObservedEvent.UserPresent, At(8, 46)),
            .. Ticks(At(8, 45), At(17)),
            Seen(ObservedEvent.DisplayOff, At(17)),
        ];

        var active = WorkIntervals.Build(observations, Defaults).Active;

        Assert.Equal([new Interval(At(8), At(17))], active);
    }

    [Fact]
    public void Build_OutageLongerThanTheAllowance_StaysExcluded()
    {
        Observation[] observations =
        [
            Seen(ObservedEvent.DisplayOn, At(8)),
            Seen(ObservedEvent.UserPresent, At(8)),
            .. Ticks(At(8), At(8, 30)),
            Seen(ObservedEvent.AgentStarted, At(9, 15)),
            Seen(ObservedEvent.DisplayOn, At(9, 15)),
            Seen(ObservedEvent.UserPresent, At(9, 16)),
            .. Ticks(At(9, 15), At(17)),
            Seen(ObservedEvent.DisplayOff, At(17)),
        ];

        var active = WorkIntervals.Build(observations, Defaults).Active;

        Assert.Equal(
            [new Interval(At(8), At(8, 30)), new Interval(At(9, 15), At(17))],
            active);
    }

    // An outage with nothing after it is not a restart. Nothing says the user stayed.
    [Fact]
    public void Build_OutageAtTheEndOfTheDay_IsNotCountedAsWork()
    {
        Observation[] observations =
        [
            Seen(ObservedEvent.DisplayOn, At(8)),
            Seen(ObservedEvent.UserPresent, At(8)),
            .. Ticks(At(8), At(16)),
        ];

        var active = WorkIntervals.Build(observations, Defaults).Active;

        Assert.Equal([new Interval(At(8), At(16))], active);
    }
}
