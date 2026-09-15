namespace MooTrack.Derivation;

public enum TimelineKind
{
    Work,
    Break,
}

public sealed record TimelineEntry(
    DateOnly Date, TimelineKind Kind, Interval Span, string Cause)
{
    public decimal Minutes => Math.Round((decimal)Span.Duration.TotalMinutes, 1);
}

/// <summary>
/// A day laid out as it was lived: working blocks and the breaks between them.
/// </summary>
/// <remarks>
/// The daily totals are sums of these. A total on its own cannot be checked against
/// anyone's memory of a day, and a figure that cannot be checked is a figure that
/// cannot be defended.
/// </remarks>
public static class DayTimeline
{
    public const string Away = "away";
    public const string RecorderDown = "recorder down";

    public static IReadOnlyList<TimelineEntry> Build(
        IEnumerable<Interval> active, IEnumerable<Interval> gaps)
    {
        var outages = gaps.ToList();

        return
        [
            .. IntervalSet.SplitAtMidnight(active)
                .GroupBy(i => DateOnly.FromDateTime(i.Start.Date))
                .OrderBy(g => g.Key)
                .SelectMany(day => Lay(day.Key, [.. day.OrderBy(i => i.Start)], outages))
        ];
    }

    private static IEnumerable<TimelineEntry> Lay(
        DateOnly date, IReadOnlyList<Interval> sessions, List<Interval> outages)
    {
        for (var i = 0; i < sessions.Count; i++)
        {
            yield return new TimelineEntry(date, TimelineKind.Work, sessions[i], String.Empty);

            if (i + 1 == sessions.Count) continue;

            var between = new Interval(sessions[i].End, sessions[i + 1].Start);
            yield return new TimelineEntry(date, TimelineKind.Break, between, Cause(between, outages));
        }
    }

    private static string Cause(Interval between, List<Interval> outages) =>
        outages.Any(o => o.Start <= between.Start && o.End >= between.End)
            ? RecorderDown
            : Away;
}
