using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class NdjsonWriterTests
{
    private static readonly Observation Sample = new()
    {
        Timestamp = new DateTimeOffset(2026, 8, 18, 8, 14, 3, TimeSpan.FromHours(10)),
        UnbiasedMs = 987654321,
        Event = ObservedEvent.DisplayOn,
        Host = "WORKSTATION",
        User = "user",
        Source = "PowerNotify",
        Detail = "console",
    };

    [Fact]
    public void Line_RoundTripsThroughTheReader()
    {
        var parsed = NdjsonReader.Parse([NdjsonWriter.Line(Sample)]);

        Assert.Empty(parsed.Malformed);
        Assert.Equal(Sample, Assert.Single(parsed.Observations));
    }

    [Fact]
    public void Line_IsSingleLineWithNoEmbeddedNewline()
    {
        var line = NdjsonWriter.Line(Sample with { Detail = "two\nlines" });

        Assert.DoesNotContain('\n', line);
        Assert.Single(NdjsonReader.Parse([line]).Observations);
    }

    [Fact]
    public void Line_PreservesTheRecordedUtcOffset()
    {
        var line = NdjsonWriter.Line(Sample);

        Assert.Contains("\"tsUtc\":\"2026-08-17T22:14:03Z\"", line);
        Assert.Contains("\"tsOffset\":\"+10:00\"", line);
    }

    [Fact]
    public void DedupeKey_IsStableForTheSameSecond()
    {
        var later = Sample with { Timestamp = Sample.Timestamp.AddMilliseconds(400) };

        Assert.Equal(NdjsonWriter.DedupeKey(Sample), NdjsonWriter.DedupeKey(later));
    }

    [Fact]
    public void DedupeKey_DiffersByEvent()
    {
        var other = Sample with { Event = ObservedEvent.DisplayOff };

        Assert.NotEqual(NdjsonWriter.DedupeKey(Sample), NdjsonWriter.DedupeKey(other));
    }
}
