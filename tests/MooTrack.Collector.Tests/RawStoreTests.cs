using Microsoft.Extensions.Logging.Abstractions;
using MooTrack.Collector;
using MooTrack.Derivation;

namespace MooTrack.Collector.Tests;

public sealed class RawStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-raw").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Primary => Path.Combine(_root, "share");
    private string Mirror => Path.Combine(_root, "mirror");

    private RawStore Build(bool mirrored = false) =>
        new(Primary, mirrored ? Mirror : null, NullLogger<RawStore>.Instance);

    private static Observation At(int hour, int minute, ObservedEvent observed, string host = "WAU") =>
        new()
        {
            Timestamp = new DateTimeOffset(2026, 9, 14, hour, minute, 0, TimeSpan.FromHours(10)),
            UnbiasedMs = hour * 3_600_000 + minute * 60_000,
            Event = observed,
            Host = host,
            User = "user",
            Source = "PowerNotify",
        };

    [Fact]
    public void Append_WritesOneFilePerHostAndLocalDate()
    {
        Build().Append([At(9, 0, ObservedEvent.DisplayOn)]);

        Assert.True(File.Exists(Path.Combine(Primary, "WAU", "2026-09-14.ndjson")));
    }

    [Fact]
    public void Append_TwoHosts_AreKeptApart()
    {
        Build().Append([At(9, 0, ObservedEvent.DisplayOn), At(9, 0, ObservedEvent.DisplayOn, "RA")]);

        Assert.Equal(
            ["RA", "WAU"],
            Directory.GetDirectories(Primary).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Append_SameObservationTwice_IsStoredOnce()
    {
        var store = Build();

        var first = store.Append([At(9, 0, ObservedEvent.DisplayOn)]);
        var second = store.Append([At(9, 0, ObservedEvent.DisplayOn)]);

        Assert.Equal(1, first.Accepted);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(1, second.Duplicates);
        Assert.Single(File.ReadAllLines(Path.Combine(Primary, "WAU", "2026-09-14.ndjson")));
    }

    [Fact]
    public void Append_DuplicateWithinOneBatch_IsStoredOnce()
    {
        var result = Build().Append(
            [At(9, 0, ObservedEvent.DisplayOn), At(9, 0, ObservedEvent.DisplayOn)]);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public void Append_DedupeSurvivesARestart()
    {
        Build().Append([At(9, 0, ObservedEvent.DisplayOn)]);

        var result = Build().Append([At(9, 0, ObservedEvent.DisplayOn)]);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public void Append_SameSecondDifferentEvent_IsNotADuplicate()
    {
        var store = Build();

        store.Append([At(9, 0, ObservedEvent.DisplayOn)]);
        var result = store.Append([At(9, 0, ObservedEvent.UserPresent)]);

        Assert.Equal(1, result.Accepted);
    }

    [Fact]
    public void Append_WithMirrorConfigured_WritesBothCopies()
    {
        Build(mirrored: true).Append([At(9, 0, ObservedEvent.DisplayOn)]);

        Assert.Equal(
            File.ReadAllLines(Path.Combine(Primary, "WAU", "2026-09-14.ndjson")),
            File.ReadAllLines(Path.Combine(Mirror, "WAU", "2026-09-14.ndjson")));
    }

    [Fact]
    public void Observations_ReadsBackEverythingStored()
    {
        var store = Build();
        store.Append([At(9, 0, ObservedEvent.DisplayOn), At(17, 0, ObservedEvent.DisplayOff)]);

        var read = store.Observations().ToList();

        Assert.Equal(
            [ObservedEvent.DisplayOn, ObservedEvent.DisplayOff],
            read.Select(o => o.Event));
    }

    [Fact]
    public void WriteMountProbe_LeavesAFileTheShareCanBeCheckedFor()
    {
        var store = Build();

        var probe = store.WriteMountProbe();

        var path = Path.Combine(Primary, RawStore.ProbeFileName);
        Assert.True(File.Exists(path));
        Assert.Contains(probe, File.ReadAllText(path));
    }
}
