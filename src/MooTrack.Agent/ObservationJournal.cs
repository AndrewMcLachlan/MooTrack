using System.Globalization;
using System.Text;
using MooTrack.Derivation;

namespace MooTrack.Agent;

public sealed class ObservationJournal(string root)
{
    private readonly Lock _gate = new();

    // The local file is the commit point, so a write that is merely buffered is a
    // write that a power loss erases. Flush every line.
    public void Append(Observation observation)
    {
        var path = PathFor(observation.Timestamp);
        var line = NdjsonWriter.Line(observation) + Environment.NewLine;

        lock (_gate)
        {
            Directory.CreateDirectory(root);
            using var file = new FileStream(
                path, FileMode.Append, FileAccess.Write, FileShare.Read);
            file.Write(Encoding.UTF8.GetBytes(line));
            file.Flush(flushToDisk: true);
        }
    }

    public string PathFor(DateTimeOffset timestamp) =>
        Path.Combine(root, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".ndjson");
}
