using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class DayTimelineTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(10);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 9, day, hour, minute, 0, Offset);

    [Fact]
    public void Build_TwoSessions_PutsABreakBetweenThem()
    {
        var timeline = DayTimeline.Build(
            [new Interval(At(15, 9), At(15, 12)), new Interval(At(15, 13), At(15, 17))], []);

        Assert.Equal(
            [TimelineKind.Work, TimelineKind.Break, TimelineKind.Work],
            timeline.Select(e => e.Kind));
        Assert.Equal(60, timeline[1].Minutes);
    }

    [Fact]
    public void Build_DoesNotInventABreakAfterTheLastSession()
    {
        var timeline = DayTimeline.Build([new Interval(At(15, 9), At(15, 17))], []);

        Assert.Equal(TimelineKind.Work, Assert.Single(timeline).Kind);
    }

    // The distinction that matters when checking a day against memory: time you were
    // away, versus time nobody was watching.
    [Fact]
    public void Build_ABreakCoveredByARecorderGap_IsNamedAsOne()
    {
        var timeline = DayTimeline.Build(
            [new Interval(At(15, 8), At(15, 8, 30)), new Interval(At(15, 8, 45), At(15, 17))],
            [new Interval(At(15, 8, 30), At(15, 8, 45))]);

        Assert.Equal("recorder down", timeline[1].Cause);
    }

    [Fact]
    public void Build_AnOrdinaryBreak_IsNamedAsTimeAway()
    {
        var timeline = DayTimeline.Build(
            [new Interval(At(15, 9), At(15, 12)), new Interval(At(15, 13), At(15, 17))], []);

        Assert.Equal("away", timeline[1].Cause);
    }

    [Fact]
    public void Build_SessionsOnTwoDays_DoesNotBridgeOvernight()
    {
        var timeline = DayTimeline.Build(
            [new Interval(At(15, 9), At(15, 17)), new Interval(At(16, 9), At(16, 17))], []);

        Assert.All(timeline, e => Assert.Equal(TimelineKind.Work, e.Kind));
        Assert.Equal([new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)],
            timeline.Select(e => e.Date));
    }

    [Fact]
    public void Build_ASessionCrossingMidnight_IsSplit()
    {
        var timeline = DayTimeline.Build([new Interval(At(15, 22), At(16, 2))], []);

        Assert.Equal(2, timeline.Count);
        Assert.Equal(120, timeline[0].Minutes);
        Assert.Equal(120, timeline[1].Minutes);
    }
}
