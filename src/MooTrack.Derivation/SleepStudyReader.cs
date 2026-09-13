using System.Globalization;
using System.Text.RegularExpressions;

namespace MooTrack.Derivation;

public static partial class SleepStudyReader
{
    public static IReadOnlyList<Interval> ReadActiveIntervals(string path, TimeSpan offset)
    {
        var raw = File.ReadAllText(path);
        var open = raw.IndexOf("<ScenarioInstances>", StringComparison.Ordinal);
        var close = raw.IndexOf("</ScenarioInstances>", StringComparison.Ordinal);
        if (open < 0 || close < 0)
            throw new InvalidDataException($"no <ScenarioInstances> block in {path}");

        var block = raw[open..close];
        var intervals = new List<Interval>();

        foreach (var element in Elements(block, "RecentUsageInstance"))
        {
            if (element.GetValueOrDefault("Type") != "Active") continue;
            var start = Timestamp(element["LocalTimestamp"], offset);
            intervals.Add(new Interval(start, start + Ticks(element["Duration"])));
        }

        foreach (var element in Elements(block, "OsStateInstance"))
        {
            if (element.GetValueOrDefault("Type") != "Active") continue;
            intervals.Add(new Interval(
                Timestamp(element["LocalTimestamp"], offset),
                Timestamp(element["ExitLocalTimestamp"], offset)));
        }

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        return intervals;
    }

    static IEnumerable<Dictionary<string, string>> Elements(string block, string tag) =>
        Regex.Matches(block, $@"<{tag}\s(.*?)>", RegexOptions.Singleline)
            .Select(m => AttributePattern().Matches(m.Groups[1].Value)
                .ToDictionary(a => a.Groups[1].Value, a => a.Groups[2].Value));

    static DateTimeOffset Timestamp(string value, TimeSpan offset) =>
        new(DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), offset);

    // Duration is in 100ns ticks; a wrong unit here silently rescales every day's hours.
    static TimeSpan Ticks(string value) =>
        TimeSpan.FromTicks((long)Double.Parse(value, CultureInfo.InvariantCulture));

    [GeneratedRegex(@"(\w+)=""([^""]*)""")]
    private static partial Regex AttributePattern();
}
