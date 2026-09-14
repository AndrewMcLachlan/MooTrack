using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class DailyReportTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(10);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 8, day, hour, minute, 0, Offset);

    private static readonly DerivationOptions Defaults = new();

    [Fact]
    public void Build_TwoSessionsWithLunch_ReportsActiveSpanAndBreak()
    {
        Interval[] active = [new(At(17, 9), At(17, 12)), new(At(17, 13), At(17, 17))];

        var day = Assert.Single(DailyReport.Build(active, Defaults));

        Assert.Equal(new DateOnly(2026, 8, 17), day.Date);
        Assert.Equal(7.00m, day.ActiveHours);
        Assert.Equal(8.00m, day.SpanHours);
        Assert.Equal(1.00m, day.AwayHours);
        Assert.Equal(2, day.Sessions);
        Assert.Equal(60, day.LongestBreakMinutes);
        Assert.Equal(0, day.FringeSessions);
    }

    [Fact]
    public void Build_FringeSessionLate_ExcludedFromSpanButCountedInHours()
    {
        Interval[] active =
        [
            new(At(17, 9), At(17, 17)),
            new(At(17, 23), At(17, 23).AddMinutes(3))
        ];

        var day = Assert.Single(DailyReport.Build(active, Defaults));

        Assert.Equal(new TimeOnly(17, 0), day.LastActive);
        Assert.Equal(8.05m, day.ActiveHours);
        Assert.Equal(1, day.FringeSessions);
    }

    [Fact]
    public void Build_SessionsOnTwoDays_ReportsOneRecordEach()
    {
        Interval[] active = [new(At(17, 9), At(17, 17)), new(At(18, 9), At(18, 17))];

        var days = DailyReport.Build(active, Defaults);

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 8, 18), days[1].Date);
    }
}
