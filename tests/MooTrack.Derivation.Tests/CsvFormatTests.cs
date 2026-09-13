using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class CsvFormatTests
{
    static readonly DayRecord Worked = new()
    {
        Date = new DateOnly(2026, 8, 17),
        FirstActive = new TimeOnly(8, 14, 37),
        LastActive = new TimeOnly(18, 19, 2),
        SpanHours = 10.09m,
        ActiveHours = 7.43m,
        AwayHours = 2.67m,
        Sessions = 17,
        LongestBreakMinutes = 75,
        FringeSessions = 1,
        Quality = DayQuality.Complete,
        UnaccountedMinutes = 0,
    };

    [Fact]
    public void DailyRow_WorkedDay_WritesSecondPrecisionTimes()
    {
        Assert.Equal(
            "2026-08-17,Mon,08:14:37,18:19:02,10.09,7.43,2.67,17,75,1,Complete,0",
            CsvFormat.Row(Worked));
    }

    [Fact]
    public void DailyRow_SilentDay_LeavesTimesEmpty()
    {
        var silent = Worked with
        {
            FirstActive = null,
            LastActive = null,
            SpanHours = 0m,
            ActiveHours = 0m,
            AwayHours = 0m,
            Sessions = 0,
            LongestBreakMinutes = 0,
            FringeSessions = 0,
            Quality = DayQuality.Unreliable,
            UnaccountedMinutes = 480,
        };

        Assert.Equal(
            "2026-08-17,Mon,,,0.00,0.00,0.00,0,0,0,Unreliable,480",
            CsvFormat.Row(silent));
    }

    [Fact]
    public void DailyHeader_MatchesRowWidth()
    {
        Assert.Equal(
            CsvFormat.DailyHeader.Split(',').Length,
            CsvFormat.Row(Worked).Split(',').Length);
    }

    [Fact]
    public void WeeklyRow_WritesIsoWeekAndTotals()
    {
        var week = new WeekRecord
        {
            IsoWeek = "2026-W34",
            WeekStart = new DateOnly(2026, 8, 17),
            ActiveHours = 38.11m,
            DaysWithActivity = 5,
            MeanHoursPerDay = 7.62m,
            TotalSpanHours = 55.06m,
            PartialDays = 0,
            UnreliableDays = 0,
        };

        Assert.Equal("2026-W34,2026-08-17,38.11,5,7.62,55.06,0,0", CsvFormat.Row(week));
    }
}
