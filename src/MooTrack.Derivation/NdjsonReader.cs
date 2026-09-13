using System.Globalization;
using System.Text.Json;

namespace MooTrack.Derivation;

public static class NdjsonReader
{
    public static ObservationLog ReadFile(string path) =>
        Parse(File.ReadLines(path));

    public static ObservationLog Parse(IEnumerable<string> lines)
    {
        var observations = new List<Observation>();
        var malformed = new List<string>();

        foreach (var line in lines)
        {
            if (String.IsNullOrWhiteSpace(line)) continue;

            var observation = TryParse(line);
            if (observation is null) malformed.Add(line);
            else observations.Add(observation);
        }

        observations.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return new ObservationLog(observations, malformed);
    }

    static Observation? TryParse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!Enum.TryParse<ObservedEvent>(Text(root, "event"), out var observed))
                return null;

            var utc = DateTimeOffset.Parse(
                Text(root, "tsUtc"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            return new Observation
            {
                Timestamp = utc.ToOffset(Offset(Text(root, "tsOffset"))),
                UnbiasedMs = root.TryGetProperty("unbiasedMs", out var ms) ? ms.GetInt64() : 0,
                Event = observed,
                Host = Text(root, "host"),
                User = Text(root, "user"),
                Source = Text(root, "source"),
                Detail = Text(root, "detail"),
            };
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetString() ?? String.Empty : String.Empty;

    static TimeSpan Offset(string value) =>
        String.IsNullOrEmpty(value)
            ? TimeSpan.Zero
            : TimeSpan.Parse(value.TrimStart('+'), CultureInfo.InvariantCulture);
}
