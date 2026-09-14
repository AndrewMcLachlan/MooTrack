namespace MooTrack.Derivation;

public sealed record WorkTimeline(
    IReadOnlyList<Interval> Active,
    IReadOnlyList<Interval> Covered,
    IReadOnlyList<Interval> Gaps);

public static class WorkIntervals
{
    private static readonly ObservedEvent[] Departures =
        [ObservedEvent.UserInactive, ObservedEvent.Lock, ObservedEvent.Suspend];

    // AgentStarted and AgentStopped are deliberately absent from both lists. They
    // describe the recorder, not the person: treating a restart as a return would end
    // a break at the restart rather than when the user actually came back.
    private static readonly ObservedEvent[] Returns =
        [ObservedEvent.UserPresent, ObservedEvent.Unlock, ObservedEvent.Resume, ObservedEvent.DisplayOn];

    private static readonly ObservedEvent[] Confirmations =
        [ObservedEvent.UserPresent, ObservedEvent.Unlock];

    public static WorkTimeline Build(
        IEnumerable<Observation> observations, DerivationOptions options)
    {
        var ordered = observations.OrderBy(o => o.Timestamp).ToList();
        if (ordered.Count == 0) return new WorkTimeline([], [], []);

        var covered = Coverage(ordered, options);
        var gaps = Between(covered);

        var lit = Confirmed(DisplayOn(ordered), ordered, options);
        var away = AwayEpisodes(ordered);

        var locks = options.BridgeAcrossLock
            ? []
            : ordered.Where(o => o.Event == ObservedEvent.Lock).Select(o => o.Timestamp);

        var boundaries = locks.Concat(gaps.Select(g => g.Start)).ToList();

        var active = IntervalSet.Bridge(
            IntervalSet.Subtract(lit, away), options.BridgeThreshold, boundaries);

        return new WorkTimeline(active, covered, gaps);
    }

    private static List<Interval> DisplayOn(List<Observation> ordered)
    {
        var lit = new List<Interval>();
        DateTimeOffset? opened = null;

        foreach (var observation in ordered)
        {
            if (observation.Event == ObservedEvent.DisplayOn) opened ??= observation.Timestamp;
            else if (observation.Event == ObservedEvent.DisplayOff && opened is { } start)
            {
                lit.Add(new Interval(start, observation.Timestamp));
                opened = null;
            }
        }

        if (opened is { } dangling && dangling < ordered[^1].Timestamp)
            lit.Add(new Interval(dangling, ordered[^1].Timestamp));

        return lit;
    }

    private static List<Interval> Confirmed(
        List<Interval> lit, List<Observation> ordered, DerivationOptions options)
    {
        var confirmations = ordered
            .Where(o => Confirmations.Contains(o.Event))
            .Select(o => o.Timestamp)
            .ToList();

        return [.. lit.Where(span => confirmations.Any(
            at => at >= span.Start && at <= span.Start + options.ConfirmationWindow))];
    }

    private static List<Interval> AwayEpisodes(List<Observation> ordered)
    {
        var episodes = new List<Interval>();
        DateTimeOffset? left = null;

        foreach (var observation in ordered)
        {
            if (left is null && Departures.Contains(observation.Event))
                left = observation.Timestamp;
            else if (left is { } start && Returns.Contains(observation.Event))
            {
                episodes.Add(new Interval(start, observation.Timestamp));
                left = null;
            }
        }

        if (left is { } unresolved && unresolved < ordered[^1].Timestamp)
            episodes.Add(new Interval(unresolved, ordered[^1].Timestamp));

        return episodes;
    }

    // Only the tick proves the recorder was alive; a quiet period between real
    // events is not evidence of an outage, and billing it as one loses real hours.
    private static List<Interval> Coverage(List<Observation> ordered, DerivationOptions options)
    {
        var ticks = ordered
            .Where(o => o.Event == ObservedEvent.Tick)
            .Select(o => o.Timestamp)
            .ToList();

        if (ticks.Count == 0) return [new Interval(ordered[0].Timestamp, ordered[^1].Timestamp)];

        var tolerance = options.TickInterval + options.GapTolerance;
        var covered = new List<Interval>();
        var start = ticks[0];
        var last = start;

        foreach (var tick in ticks.Skip(1))
        {
            if (tick - last > tolerance)
            {
                covered.Add(new Interval(start, last));
                start = tick;
            }
            last = tick;
        }

        covered.Add(new Interval(start, last));

        covered[0] = covered[0] with { Start = Earliest(ordered[0].Timestamp, covered[0].Start) };
        covered[^1] = covered[^1] with { End = Latest(ordered[^1].Timestamp, covered[^1].End) };
        return covered;
    }

    private static DateTimeOffset Earliest(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static List<Interval> Between(List<Interval> covered) =>
        [.. covered.Zip(covered.Skip(1), (a, b) => new Interval(a.End, b.Start))];
}
