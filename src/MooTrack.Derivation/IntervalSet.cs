namespace MooTrack.Derivation;

public static class IntervalSet
{
    public static IReadOnlyList<Interval> Subtract(
        IEnumerable<Interval> source, IEnumerable<Interval> remove)
    {
        var cuts = Merge(remove);
        var result = new List<Interval>();

        foreach (var span in Merge(source))
        {
            var start = span.Start;
            foreach (var cut in cuts)
            {
                if (cut.End <= start) continue;
                if (cut.Start >= span.End) break;
                if (cut.Start > start) result.Add(new Interval(start, cut.Start));
                start = cut.End;
            }
            if (start < span.End) result.Add(new Interval(start, span.End));
        }

        return result;
    }


    public static IReadOnlyList<Interval> Bridge(
        IEnumerable<Interval> intervals, TimeSpan threshold) =>
        Bridge(intervals, threshold, []);

    public static IReadOnlyList<Interval> Bridge(
        IEnumerable<Interval> intervals, TimeSpan threshold,
        IEnumerable<DateTimeOffset> hardBoundaries)
    {
        var boundaries = hardBoundaries.ToList();
        var result = new List<Interval>();

        foreach (var span in Merge(intervals))
        {
            var joinable = result.Count > 0
                && span.Start - result[^1].End < threshold
                && !boundaries.Any(at => at >= result[^1].End && at <= span.Start);

            if (joinable) result[^1] = result[^1] with { End = span.End };
            else result.Add(span);
        }

        return result;
    }


    public static IReadOnlyList<Interval> SplitAtMidnight(IEnumerable<Interval> intervals)
    {
        var result = new List<Interval>();
        foreach (var span in Merge(intervals))
        {
            var start = span.Start;
            while (start.Date < span.End.Date)
            {
                var midnight = new DateTimeOffset(start.Date.AddDays(1), start.Offset);
                if (midnight >= span.End) break;
                result.Add(new Interval(start, midnight));
                start = midnight;
            }
            if (start < span.End) result.Add(new Interval(start, span.End));
        }
        return result;
    }

    static List<Interval> Merge(IEnumerable<Interval> intervals)
    {
        var merged = new List<Interval>();
        foreach (var span in intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                if (span.End > merged[^1].End) merged[^1] = merged[^1] with { End = span.End };
            }
            else merged.Add(span);
        }
        return merged;
    }
}
