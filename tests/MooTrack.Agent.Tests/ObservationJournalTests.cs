using MooTrack.Agent;
using MooTrack.Derivation;

namespace MooTrack.Agent.Tests;

public sealed class ObservationJournalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-journal").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Observation At(DateTimeOffset timestamp, ObservedEvent observed) =>
        new()
        {
            Timestamp = timestamp,
            UnbiasedMs = 1,
            Event = observed,
            Host = "WORKSTATION",
            User = "user",
            Source = "PowerNotify",
        };

    private static DateTimeOffset Local(int day, int hour) =>
        new(2026, 8, day, hour, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Append_NamesTheFileByLocalDate()
    {
        var journal = new ObservationJournal(_root);

        journal.Append(At(Local(17, 23), ObservedEvent.DisplayOff));

        Assert.Equal(["2026-08-17.ndjson"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void Append_SpanningMidnight_WritesOneFilePerDay()
    {
        var journal = new ObservationJournal(_root);

        journal.Append(At(Local(17, 23), ObservedEvent.DisplayOn));
        journal.Append(At(Local(18, 1), ObservedEvent.DisplayOff));

        Assert.Equal(
            ["2026-08-17.ndjson", "2026-08-18.ndjson"],
            Directory.GetFiles(_root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Append_ToAnExistingDay_AddsWithoutTruncating()
    {
        var journal = new ObservationJournal(_root);

        journal.Append(At(Local(17, 9), ObservedEvent.DisplayOn));
        journal.Append(At(Local(17, 17), ObservedEvent.DisplayOff));

        Assert.Equal(2, File.ReadAllLines(Path.Combine(_root, "2026-08-17.ndjson")).Length);
    }

    [Fact]
    public void Append_SurvivesAReopenedJournal()
    {
        new ObservationJournal(_root).Append(At(Local(17, 9), ObservedEvent.DisplayOn));
        new ObservationJournal(_root).Append(At(Local(17, 17), ObservedEvent.DisplayOff));

        var log = NdjsonReader.ReadFile(Path.Combine(_root, "2026-08-17.ndjson"));

        Assert.Empty(log.Malformed);
        Assert.Equal(
            [ObservedEvent.DisplayOn, ObservedEvent.DisplayOff],
            log.Observations.Select(o => o.Event));
    }

    [Fact]
    public void Append_IsReadableImmediatelyByAnotherReader()
    {
        var journal = new ObservationJournal(_root);

        journal.Append(At(Local(17, 9), ObservedEvent.DisplayOn));

        var log = NdjsonReader.ReadFile(Path.Combine(_root, "2026-08-17.ndjson"));
        Assert.Single(log.Observations);
    }
}
