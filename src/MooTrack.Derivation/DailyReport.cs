namespace MooTrack.Derivation;

public static class DailyReport
{
    public static IReadOnlyList<DayRecord> Build(
        IEnumerable<Interval> active, DerivationOptions options) =>
        Build(new WorkTimeline([.. active], [], []), options);

    public static IReadOnlyList<DayRecord> Build(
        WorkTimeline timeline, DerivationOptions options)
    {
        var sessionsByDay = IntervalSet.SplitAtMidnight(timeline.Active)
            .GroupBy(i => DateOnly.FromDateTime(i.Start.Date))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Interval>)[.. g.OrderBy(i => i.Start)]);

        var gapsByDay = IntervalSet.SplitAtMidnight(timeline.Gaps)
            .GroupBy(i => DateOnly.FromDateTime(i.Start.Date))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Interval>)[.. g.OrderBy(i => i.Start)]);

        return
        [
            .. sessionsByDay.Keys.Union(gapsByDay.Keys).OrderBy(d => d).Select(date =>
                BuildDay(
                    date,
                    sessionsByDay.GetValueOrDefault(date, []),
                    gapsByDay.GetValueOrDefault(date, []),
                    options))
        ];
    }

    private static DayRecord BuildDay(
        DateOnly date, IReadOnlyList<Interval> sessions, IReadOnlyList<Interval> gaps,
        DerivationOptions options)
    {
        if (sessions.Count == 0) return Silent(date, Total(gaps));

        var core = DayShape.TrimFringe(sessions, options.FringeMaxDuration, options.FringeGap);

        var first = core[0].Start;
        var last = core[^1].End;
        var span = last - first;
        var coreTotal = Total(core);
        var unaccounted = Overlap(gaps, new Interval(first, last));
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
            Quality = Rate(unaccounted, span),
            UnaccountedMinutes = Minutes(unaccounted),
        };
    }

    private static DayRecord Silent(DateOnly date, TimeSpan unaccounted) =>
        new()
        {
            Date = date,
            FirstActive = null,
            LastActive = null,
            SpanHours = 0m,
            ActiveHours = 0m,
            AwayHours = 0m,
            Sessions = 0,
            LongestBreakMinutes = 0,
            FringeSessions = 0,
            Quality = DayQuality.Unreliable,
            UnaccountedMinutes = Minutes(unaccounted),
        };

    // Coverage is judged against the working day, not the calendar day: a recorder
    // idle overnight says nothing about whether the day's hours are evidenced. A gap
    // that opens before the day's last activity is counted to its full length, since
    // it leaves the end of that day unevidenced.
    private static TimeSpan Overlap(IEnumerable<Interval> gaps, Interval span) =>
        gaps.Aggregate(TimeSpan.Zero, (sum, gap) =>
        {
            if (gap.Start > span.End) return sum;
            var start = gap.Start > span.Start ? gap.Start : span.Start;
            return gap.End > start ? sum + (gap.End - start) : sum;
        });

    private static DayQuality Rate(TimeSpan unaccounted, TimeSpan span) =>
        unaccounted <= TimeSpan.Zero ? DayQuality.Complete
        : unaccounted.TotalMinutes * 3 > span.TotalMinutes ? DayQuality.Unreliable
        : DayQuality.Partial;

    private static int Minutes(TimeSpan span) =>
        (int)Math.Round(span.TotalMinutes, MidpointRounding.ToEven);

    private static TimeSpan Total(IEnumerable<Interval> intervals) =>
        intervals.Aggregate(TimeSpan.Zero, (sum, i) => sum + i.Duration);

    private static decimal Hours(TimeSpan span) =>
        Math.Round((decimal)span.TotalHours, 2, MidpointRounding.ToEven);
}
