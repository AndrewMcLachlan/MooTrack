using MooTrack.Collector;
using MooTrack.Derivation;

namespace MooTrack.Collector.Tests;

public sealed class RawStoreRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-recovery").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Store => Path.Combine(_root, "raw");

    private static Observation At(int minute) =>
        new()
        {
            Timestamp = new DateTimeOffset(2026, 9, 14, 9, minute, 0, TimeSpan.FromHours(10)),
            UnbiasedMs = minute * 60_000,
            Event = ObservedEvent.Tick,
            Host = "HOST",
            User = "user",
            Source = "Tick",
        };

    /// <summary>
    /// The failure that lost a day. A write that throws must not leave the collector
    /// believing it holds those observations: the agent retries, every one comes back
    /// a duplicate, the response is a cheerful 200, and the data never lands.
    /// </summary>
    [Fact]
    public void Append_AfterAFailedWrite_StillAcceptsTheSameObservations()
    {
        Directory.CreateDirectory(Store);

        // A file where the host directory needs to be: the write throws, exactly as a
        // permission failure does.
        File.WriteAllText(Path.Combine(Store, "HOST"), "in the way");

        var store = new RawStore(Store);
        Assert.ThrowsAny<Exception>(() => store.Append([At(1), At(2), At(3)]));

        File.Delete(Path.Combine(Store, "HOST"));

        var result = store.Append([At(1), At(2), At(3)]);

        Assert.Equal(3, result.Accepted);
        Assert.Equal(0, result.Duplicates);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(Store, "HOST", "2026-09-14.ndjson")).Length);
    }

    [Fact]
    public void Append_AfterAFailedWrite_DoesNotClaimToHoldThem()
    {
        Directory.CreateDirectory(Store);
        File.WriteAllText(Path.Combine(Store, "HOST"), "in the way");

        var store = new RawStore(Store);
        Assert.ThrowsAny<Exception>(() => store.Append([At(1)]));

        File.Delete(Path.Combine(Store, "HOST"));
        store.Append([At(1)]);

        Assert.Single(store.Observations());
    }
}
