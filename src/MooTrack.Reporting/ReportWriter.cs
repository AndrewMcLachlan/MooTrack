using MooTrack.Derivation;

namespace MooTrack.Reporting;

public sealed record ReportPaths(string DailyCsv, string WeeklyCsv, string Workbook);

public static class ReportWriter
{
    public const string DailyCsvName = "daily-hours.csv";
    public const string WeeklyCsvName = "weekly-hours.csv";
    public const string WorkbookName = "mootrack-hours.xlsx";

    /// <summary>
    /// Writes the derived outputs. The CSVs carry the figures and are written first.
    /// The workbook is best-effort: it can be held open by a spreadsheet, and it needs
    /// fonts present to measure columns. Neither is a reason to lose the whole report.
    /// </summary>
    public static ReportPaths WriteAll(
        string outputDirectory,
        IReadOnlyList<DayRecord> days,
        IReadOnlyList<WeekRecord> weeks,
        DerivationOptions options,
        Action<Exception>? onWorkbookFailure = null)
    {
        Directory.CreateDirectory(outputDirectory);

        var paths = new ReportPaths(
            Path.Combine(outputDirectory, DailyCsvName),
            Path.Combine(outputDirectory, WeeklyCsvName),
            Path.Combine(outputDirectory, WorkbookName));

        WriteCsv(paths.DailyCsv, CsvFormat.DailyHeader, days.Select(CsvFormat.Row));
        WriteCsv(paths.WeeklyCsv, CsvFormat.WeeklyHeader, weeks.Select(CsvFormat.Row));

        try
        {
            Workbook.Write(paths.Workbook, days, weeks, options);
        }
        catch (Exception e)
        {
            onWorkbookFailure?.Invoke(e);
        }

        return paths;
    }

    public static void WriteCsv(string path, string header, IEnumerable<string> rows)
    {
        var temporary = path + ".tmp";
        File.WriteAllLines(temporary, rows.Prepend(header));
        File.Move(temporary, path, overwrite: true);
    }
}
