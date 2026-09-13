using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class WeeklyReportTests
{
    static readonly TimeSpan Offset = TimeSpan.FromHours(10);
    static readonly DerivationOptions Defaults = new();

    static DateTimeOffset At(int day, int hour) =>
        new(2026, 8, day, hour, 0, 0, Offset);

    [Fact]
    public void Build_TwoDaysInTheSameWeek_RollsIntoOneRecord()
    {
        var days = DailyReport.Build(
            new Interval[] { new(At(17, 9), At(17, 17)), new(At(18, 9), At(18, 16)) },
            Defaults);

        var week = Assert.Single(WeeklyReport.Build(days));

        Assert.Equal("2026-W34", week.IsoWeek);
        Assert.Equal(new DateOnly(2026, 8, 17), week.WeekStart);
        Assert.Equal(15.00m, week.ActiveHours);
        Assert.Equal(2, week.DaysWithActivity);
        Assert.Equal(7.50m, week.MeanHoursPerDay);
    }

    [Fact]
    public void Build_DaysInDifferentWeeks_AreSeparateRecords()
    {
        var days = DailyReport.Build(
            new Interval[] { new(At(21, 9), At(21, 17)), new(At(25, 9), At(25, 17)) },
            Defaults);

        var weeks = WeeklyReport.Build(days);

        Assert.Equal(["2026-W34", "2026-W35"], weeks.Select(w => w.IsoWeek));
    }

    [Fact]
    public void Build_DayWithNoActivity_IsNotCountedAsAWorkingDay()
    {
        var timeline = new WorkTimeline(
            [new Interval(At(17, 9), At(17, 17))],
            [],
            [new Interval(At(18, 0), At(18, 6))]);

        var week = Assert.Single(WeeklyReport.Build(DailyReport.Build(timeline, Defaults)));

        Assert.Equal(1, week.DaysWithActivity);
        Assert.Equal(1, week.UnreliableDays);
    }
}
