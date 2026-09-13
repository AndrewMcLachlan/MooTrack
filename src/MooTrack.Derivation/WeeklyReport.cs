using System.Globalization;

namespace MooTrack.Derivation;

public sealed record WeekRecord
{
    public required string IsoWeek { get; init; }
    public required DateOnly WeekStart { get; init; }
    public required decimal ActiveHours { get; init; }
    public required int DaysWithActivity { get; init; }
    public required decimal MeanHoursPerDay { get; init; }
    public required decimal TotalSpanHours { get; init; }
    public required int PartialDays { get; init; }
    public required int UnreliableDays { get; init; }
}

public static class WeeklyReport
{
    public static IReadOnlyList<WeekRecord> Build(IEnumerable<DayRecord> days) =>
    [
        .. days
            .GroupBy(d => ISOWeek.GetYear(ToDateTime(d.Date)) * 100
                          + ISOWeek.GetWeekOfYear(ToDateTime(d.Date)))
            .OrderBy(g => g.Key)
            .Select(Roll)
    ];

    static WeekRecord Roll(IGrouping<int, DayRecord> week)
    {
        var worked = week.Where(d => d.ActiveHours > 0m).ToList();
        var active = week.Sum(d => d.ActiveHours);

        return new WeekRecord
        {
            IsoWeek = $"{week.Key / 100}-W{week.Key % 100:D2}",
            WeekStart = DateOnly.FromDateTime(
                ISOWeek.ToDateTime(week.Key / 100, week.Key % 100, DayOfWeek.Monday)),
            ActiveHours = Round(active),
            DaysWithActivity = worked.Count,
            MeanHoursPerDay = worked.Count == 0 ? 0m : Round(active / worked.Count),
            TotalSpanHours = Round(week.Sum(d => d.SpanHours)),
            PartialDays = week.Count(d => d.Quality == DayQuality.Partial),
            UnreliableDays = week.Count(d => d.Quality == DayQuality.Unreliable),
        };
    }

    static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);
}
