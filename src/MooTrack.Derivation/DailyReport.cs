namespace MooTrack.Derivation;

public static class DailyReport
{
    public static IReadOnlyList<DayRecord> Build(
        IEnumerable<Interval> active, DerivationOptions options)
    {
        var byDay = IntervalSet.SplitAtMidnight(active)
            .GroupBy(i => DateOnly.FromDateTime(i.Start.Date))
            .OrderBy(g => g.Key);

        return [.. byDay.Select(g => BuildDay(g.Key, [.. g.OrderBy(i => i.Start)], options))];
    }

    static DayRecord BuildDay(
        DateOnly date, IReadOnlyList<Interval> sessions, DerivationOptions options)
    {
        var core = DayShape.TrimFringe(sessions, options.FringeMaxDuration, options.FringeGap);

        var first = core[0].Start;
        var last = core[^1].End;
        var span = last - first;
        var coreTotal = Total(core);

        var breaks = core.Zip(core.Skip(1), (a, b) => b.Start - a.End);

        return new DayRecord
        {
            Date = date,
            FirstActive = TimeOnly.FromTimeSpan(first.TimeOfDay),
            LastActive = TimeOnly.FromTimeSpan(last.TimeOfDay),
            SpanHours = Hours(span),
            ActiveHours = Hours(Total(sessions)),
            AwayHours = Hours(span - coreTotal),
            Sessions = sessions.Count,
            LongestBreakMinutes = breaks.Any()
                ? (int)Math.Round(breaks.Max().TotalMinutes, MidpointRounding.ToEven)
                : 0,
            FringeSessions = sessions.Count - core.Count,
        };
    }

    static TimeSpan Total(IEnumerable<Interval> intervals) =>
        intervals.Aggregate(TimeSpan.Zero, (sum, i) => sum + i.Duration);

    static decimal Hours(TimeSpan span) =>
        Math.Round((decimal)span.TotalHours, 2, MidpointRounding.ToEven);
}
