namespace MooTrack.Agent;

public interface ICollectorClient
{
    Task<bool> SendAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken);
}

/// <summary>
/// Ships journal lines to the collector, tracking how far each day's file has been
/// accepted. The journal is the durable record, so shipping from it — rather than
/// from an in-memory queue — means a collector that arrives late, or comes back after
/// an outage, still receives everything written while it was gone.
/// </summary>
public sealed class JournalShipper(
    string journalRoot,
    PositionStore positions,
    ICollectorClient collector,
    int batchSize)
{
    public async Task ShipAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(journalRoot)) return;

        foreach (var path in Directory.GetFiles(journalRoot, "*.ndjson").Order())
        {
            if (cancellationToken.IsCancellationRequested) return;
            if (!await ShipFileAsync(path, cancellationToken)) return;
        }
    }

    private async Task<bool> ShipFileAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        var shipped = positions.LastRecordId(name) ?? 0;

        var pending = File.ReadLines(path).Skip((int)shipped).ToList();
        if (pending.Count == 0) return true;

        foreach (var batch in Chunk(pending))
        {
            if (!await collector.SendAsync(batch, cancellationToken)) return false;

            shipped += batch.Count;
            positions.Save(name, shipped);
        }

        return true;
    }

    private IEnumerable<List<string>> Chunk(List<string> lines)
    {
        for (var offset = 0; offset < lines.Count; offset += batchSize)
            yield return lines.GetRange(offset, Math.Min(batchSize, lines.Count - offset));
    }
}
