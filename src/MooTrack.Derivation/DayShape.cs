namespace MooTrack.Derivation;

public static class DayShape
{
    public static IReadOnlyList<Interval> TrimFringe(
        IEnumerable<Interval> sessions, TimeSpan maxDuration, TimeSpan minGap)
    {
        var core = sessions.OrderBy(s => s.Start).ToList();

        while (core.Count > 1
               && core[^1].Duration <= maxDuration
               && core[^1].Start - core[^2].End >= minGap)
            core.RemoveAt(core.Count - 1);

        while (core.Count > 1
               && core[0].Duration <= maxDuration
               && core[1].Start - core[0].End >= minGap)
            core.RemoveAt(0);

        return core;
    }
}
