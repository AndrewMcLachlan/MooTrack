using System.Globalization;
using System.Text;
using MooTrack.Derivation;

namespace MooTrack.Collector;

public sealed record IngestResult(int Accepted, int Duplicates);

/// <summary>
/// The durable store: append-only NDJSON, one file per host per local day.
/// </summary>
public sealed class RawStore(string root)
{
    private readonly Dictionary<string, HashSet<string>> _seen = [];
    private readonly Lock _gate = new();

    public string Root => root;

    public IngestResult Append(IEnumerable<Observation> observations)
    {
        var accepted = 0;
        var duplicates = 0;

        lock (_gate)
        {
            foreach (var group in observations.GroupBy(FileKey))
            {
                var keys = KnownKeys(group.Key);
                var lines = new List<string>();

                foreach (var observation in group)
                {
                    if (!keys.Add(DedupeKey(observation)))
                    {
                        duplicates++;
                        continue;
                    }

                    lines.Add(NdjsonWriter.Line(observation));
                    accepted++;
                }

                if (lines.Count > 0) AppendLines(group.Key, lines);
            }
        }

        return new IngestResult(accepted, duplicates);
    }

    public IEnumerable<Observation> Observations()
    {
        if (!Directory.Exists(root)) return [];

        var lines = Directory
            .GetFiles(root, "*.ndjson", SearchOption.AllDirectories)
            .Order()
            .SelectMany(File.ReadLines);

        return NdjsonReader.Parse(lines).Observations;
    }

    private static string FileKey(Observation observation) =>
        Path.Combine(
            Sanitise(observation.Host),
            observation.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".ndjson");

    private static string DedupeKey(Observation observation) =>
        String.Join('|',
            observation.Host,
            observation.Event.ToString(),
            observation.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

    private static string Sanitise(string host) =>
        String.IsNullOrWhiteSpace(host)
            ? "unknown"
            : String.Concat(host.Where(c => Char.IsLetterOrDigit(c) || c is '-' or '_'));

    private HashSet<string> KnownKeys(string fileKey)
    {
        if (_seen.TryGetValue(fileKey, out var keys)) return keys;

        keys = [];
        var path = Path.Combine(root, fileKey);

        if (File.Exists(path))
            foreach (var observation in NdjsonReader.Parse(File.ReadLines(path)).Observations)
                keys.Add(DedupeKey(observation));

        _seen[fileKey] = keys;
        return keys;
    }

    private void AppendLines(string fileKey, List<string> lines)
    {
        var path = Path.Combine(root, fileKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var payload = String.Concat(lines.Select(l => l + "\n"));

        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        file.Write(Encoding.UTF8.GetBytes(payload));
        file.Flush(flushToDisk: true);
    }
}
