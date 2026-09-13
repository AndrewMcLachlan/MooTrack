using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class NdjsonReaderTests
{
    const string DisplayOnLine =
        """
        {"tsUtc":"2026-08-17T22:14:03Z","tsOffset":"+10:00","unbiasedMs":123456,"host":"WORKSTATION","user":"user","event":"DisplayOn","source":"PowerNotify","dedupeKey":"abc","detail":""}
        """;

    [Fact]
    public void Parse_DisplayOnLine_ProjectsUtcOntoRecordedOffset()
    {
        var log = NdjsonReader.Parse([DisplayOnLine]);

        var observation = Assert.Single(log.Observations);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 18, 8, 14, 3, TimeSpan.FromHours(10)),
            observation.Timestamp);
        Assert.Equal(ObservedEvent.DisplayOn, observation.Event);
        Assert.Equal(123456, observation.UnbiasedMs);
        Assert.Equal("WORKSTATION", observation.Host);
    }

    [Fact]
    public void Parse_MalformedLine_IsReportedNotDiscarded()
    {
        var log = NdjsonReader.Parse([DisplayOnLine, "{not json", ""]);

        Assert.Single(log.Observations);
        Assert.Equal(["{not json"], log.Malformed);
    }

    [Fact]
    public void Parse_UnknownEventName_IsMalformed()
    {
        var line = DisplayOnLine.Replace("DisplayOn", "Teleported");

        var log = NdjsonReader.Parse([line]);

        Assert.Empty(log.Observations);
        Assert.Single(log.Malformed);
    }

    [Fact]
    public void Parse_OutOfOrderLines_ReturnsChronologicalOrder()
    {
        var later = DisplayOnLine.Replace("22:14:03", "23:14:03");

        var log = NdjsonReader.Parse([later, DisplayOnLine]);

        Assert.Equal(
            [new TimeOnly(8, 14, 3), new TimeOnly(9, 14, 3)],
            log.Observations.Select(o => TimeOnly.FromTimeSpan(o.Timestamp.TimeOfDay)));
    }
}
