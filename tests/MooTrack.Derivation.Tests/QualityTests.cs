using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class QualityTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(10);
    private static readonly DerivationOptions Defaults = new();

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 8, day, hour, minute, 0, Offset);

    [Fact]
    public void Build_NoRecorderGaps_DayIsComplete()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [new Interval(At(17, 0), At(17, 23, 59))],
            []);

        var day = Assert.Single(DailyReport.Build(timeline, Defaults));

        Assert.Equal(DayQuality.Complete, day.Quality);
        Assert.Equal(0, day.UnaccountedMinutes);
    }

    [Fact]
    public void Build_GapInsideWorkingDay_IsPartialAndQuantified()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [new Interval(At(17, 0), At(17, 12)), new Interval(At(17, 12, 30), At(17, 23))],
            [new Interval(At(17, 12), At(17, 12, 30))]);

        var day = Assert.Single(DailyReport.Build(timeline, Defaults));

        Assert.Equal(DayQuality.Partial, day.Quality);
        Assert.Equal(30, day.UnaccountedMinutes);
    }

    [Fact]
    public void Build_GapWithNoActivityThatDay_StillReportsTheDay()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [],
            [new Interval(At(18, 0), At(19, 0))]);

        var days = DailyReport.Build(timeline, Defaults);

        var silent = Assert.Single(days, d => d.Date == new DateOnly(2026, 8, 18));
        Assert.Equal(DayQuality.Unreliable, silent.Quality);
        Assert.Equal(0m, silent.ActiveHours);
        Assert.Null(silent.FirstActive);
    }

    [Fact]
    public void Build_GapDominatingTheDay_IsUnreliable()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 10))],
            [new Interval(At(17, 0), At(17, 9))],
            [new Interval(At(17, 10), At(17, 20))]);

        var day = Assert.Single(DailyReport.Build(timeline, Defaults));

        Assert.Equal(DayQuality.Unreliable, day.Quality);
    }

    [Fact]
    public void Build_GapOutsideWorkingHours_DoesNotDegradeQuality()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [new Interval(At(17, 8), At(17, 18))],
            [new Interval(At(17, 18), At(17, 23, 59))]);

        var day = Assert.Single(DailyReport.Build(timeline, Defaults), d => d.ActiveHours > 0m);

        Assert.Equal(DayQuality.Complete, day.Quality);
        Assert.Equal(0, day.UnaccountedMinutes);
    }

    [Fact]
    public void Build_GapOpeningInsideTheDay_CountsUntilTheRecorderReturns()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [new Interval(At(17, 8), At(17, 16))],
            [new Interval(At(17, 16), At(17, 20))]);

        var day = Assert.Single(DailyReport.Build(timeline, Defaults), d => d.ActiveHours > 0m);

        Assert.Equal(240, day.UnaccountedMinutes);
    }
}
