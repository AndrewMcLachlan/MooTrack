using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public sealed class JournalShipperTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-shipper").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeCollector : ICollectorClient
    {
        public List<string[]> Batches { get; } = [];
        public bool Accepting { get; set; } = true;

        public Task<bool> SendAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken)
        {
            if (Accepting) Batches.Add([.. lines]);
            return Task.FromResult(Accepting);
        }
    }

    private string Journal => Path.Combine(_root, "journal");
    private string Positions => Path.Combine(_root, "shipped.json");

    private void WriteDay(string date, params string[] lines)
    {
        Directory.CreateDirectory(Journal);
        File.AppendAllLines(Path.Combine(Journal, $"{date}.ndjson"), lines);
    }

    private JournalShipper Build(FakeCollector collector, int batchSize = 500) =>
        new(Journal, new PositionStore(Positions), collector, batchSize);

    [Fact]
    public async Task Ship_SendsEveryLineAlreadyInTheJournal()
    {
        WriteDay("2026-09-01", "one", "two", "three");

        var collector = new FakeCollector();
        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Equal([["one", "two", "three"]], collector.Batches);
    }

    [Fact]
    public async Task Ship_RunAgain_DoesNotResendWhatWasAccepted()
    {
        WriteDay("2026-09-01", "one", "two");

        var collector = new FakeCollector();
        await Build(collector).ShipAsync(CancellationToken.None);
        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Single(collector.Batches);
    }

    [Fact]
    public async Task Ship_AfterMoreIsAppended_SendsOnlyTheNewLines()
    {
        WriteDay("2026-09-01", "one", "two");
        var collector = new FakeCollector();
        await Build(collector).ShipAsync(CancellationToken.None);

        WriteDay("2026-09-01", "three");
        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Equal([["one", "two"], ["three"]], collector.Batches);
    }

    [Fact]
    public async Task Ship_WhenCollectorRefuses_DoesNotAdvanceAndRetriesLater()
    {
        WriteDay("2026-09-01", "one", "two");

        var collector = new FakeCollector { Accepting = false };
        await Build(collector).ShipAsync(CancellationToken.None);
        Assert.Empty(collector.Batches);

        collector.Accepting = true;
        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Equal([["one", "two"]], collector.Batches);
    }

    [Fact]
    public async Task Ship_MultipleDays_SendsOldestFirst()
    {
        WriteDay("2026-09-02", "later");
        WriteDay("2026-09-01", "earlier");

        var collector = new FakeCollector();
        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Equal([["earlier"], ["later"]], collector.Batches);
    }

    [Fact]
    public async Task Ship_BeyondBatchSize_SplitsIntoSeveralPosts()
    {
        WriteDay("2026-09-01", "a", "b", "c", "d", "e");

        var collector = new FakeCollector();
        await Build(collector, batchSize: 2).ShipAsync(CancellationToken.None);

        Assert.Equal([["a", "b"], ["c", "d"], ["e"]], collector.Batches);
    }

    [Fact]
    public async Task Ship_WithNoJournalYet_DoesNothing()
    {
        var collector = new FakeCollector();

        await Build(collector).ShipAsync(CancellationToken.None);

        Assert.Empty(collector.Batches);
    }
}
