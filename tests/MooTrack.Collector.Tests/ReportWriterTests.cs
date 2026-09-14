using MooTrack.Derivation;
using MooTrack.Reporting;

namespace MooTrack.Collector.Tests;

public sealed class ReportWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-report").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DayRecord Day = new()
    {
        Date = new DateOnly(2026, 9, 14),
        FirstActive = new TimeOnly(9, 0, 0),
        LastActive = new TimeOnly(17, 0, 0),
        SpanHours = 8.00m,
        ActiveHours = 8.00m,
        AwayHours = 0m,
        Sessions = 1,
        LongestBreakMinutes = 0,
        FringeSessions = 0,
    };

    [Fact]
    public void WriteAll_WritesBothCsvsAndTheWorkbook()
    {
        var paths = ReportWriter.WriteAll(_root, [Day], WeeklyReport.Build([Day]), new DerivationOptions());

        Assert.True(File.Exists(paths.DailyCsv));
        Assert.True(File.Exists(paths.WeeklyCsv));
        Assert.True(File.Exists(paths.Workbook));
    }

    // The workbook is the fragile output: it needs fonts present to measure columns,
    // and it is the one a spreadsheet can hold open. The CSVs carry the same figures
    // and must survive whatever the workbook does.
    [Fact]
    public void WriteAll_WhenTheWorkbookCannotBeWritten_StillWritesTheCsvs()
    {
        Directory.CreateDirectory(Path.Combine(_root, "mootrack-hours.tmp.xlsx"));
        Exception? reported = null;

        var paths = ReportWriter.WriteAll(
            _root, [Day], WeeklyReport.Build([Day]), new DerivationOptions(),
            onWorkbookFailure: e => reported = e);

        Assert.True(File.Exists(paths.DailyCsv));
        Assert.True(File.Exists(paths.WeeklyCsv));
        Assert.NotNull(reported);
    }

    [Fact]
    public void WriteAll_RewritesCleanlyOverAnExistingReport()
    {
        ReportWriter.WriteAll(_root, [Day], WeeklyReport.Build([Day]), new DerivationOptions());
        var updated = Day with { ActiveHours = 7.25m, SpanHours = 7.25m };

        var paths = ReportWriter.WriteAll(
            _root, [updated], WeeklyReport.Build([updated]), new DerivationOptions());

        Assert.Contains("7.25", File.ReadAllText(paths.DailyCsv));
        Assert.DoesNotContain("8.00", File.ReadAllText(paths.DailyCsv));
    }
}
