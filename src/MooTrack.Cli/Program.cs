using MooTrack.Cli;
using MooTrack.Derivation;

const string Usage =
    """
    mootrack --sleepstudy <report.html> --out <dir> [options]
    mootrack --ndjson <file-or-dir> --out <dir> [options]

      --offset <+HH:MM>      wall-clock offset for sleep study timestamps
      --bridge <min>         gap absorbed into a working block (default 10)
      --confirm <min>        window for a return to be confirmed (default 30)
      --fringe-max <min>     longest session treated as fringe (default 5)
      --fringe-gap <min>     isolation required for fringe (default 90)
      --gap-tolerance <min>  missing ticks before a recorder gap (default 5)
      --bridge-across-lock   treat an explicit lock as bridgeable
    """;

try
{
    var arguments = Arguments.Parse(args);
    Directory.CreateDirectory(arguments.Output);

    var days = Derive(arguments, out var malformed, out var gaps);
    var weeks = WeeklyReport.Build(days);

    Write(Path.Combine(arguments.Output, "daily-hours.csv"),
        CsvFormat.DailyHeader, days.Select(CsvFormat.Row));
    Write(Path.Combine(arguments.Output, "weekly-hours.csv"),
        CsvFormat.WeeklyHeader, weeks.Select(CsvFormat.Row));

    var workbook = Path.Combine(arguments.Output, "mootrack-hours.xlsx");
    Workbook.Write(workbook, days, weeks, arguments.Options);

    Report(days, weeks, malformed, gaps, arguments.Output);
    return 0;
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}
catch (Exception e) when (e is IOException or InvalidDataException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static IReadOnlyList<DayRecord> Derive(
    Arguments arguments, out int malformed, out int gaps)
{
    if (arguments.SleepStudy is { } report)
    {
        malformed = 0;
        gaps = 0;
        return DailyReport.Build(
            SleepStudyReader.ReadActiveIntervals(report, arguments.Offset),
            arguments.Options);
    }

    var log = ReadObservations(arguments.Ndjson!);
    var timeline = WorkIntervals.Build(log.Observations, arguments.Options);

    malformed = log.Malformed.Count;
    gaps = timeline.Gaps.Count;
    return DailyReport.Build(timeline, arguments.Options);
}

static ObservationLog ReadObservations(string path)
{
    if (File.Exists(path)) return NdjsonReader.ReadFile(path);

    if (!Directory.Exists(path))
        throw new InvalidDataException($"no such file or directory: {path}");

    var files = Directory.GetFiles(path, "*.ndjson").OrderBy(f => f).ToList();
    if (files.Count == 0)
        throw new InvalidDataException($"no .ndjson files in {path}");

    return NdjsonReader.Parse(files.SelectMany(File.ReadLines));
}

static void Write(string path, string header, IEnumerable<string> rows)
{
    var temporary = path + ".tmp";
    File.WriteAllLines(temporary, rows.Prepend(header));
    File.Move(temporary, path, overwrite: true);
}

static void Report(
    IReadOnlyList<DayRecord> days, IReadOnlyList<WeekRecord> weeks,
    int malformed, int gaps, string output)
{
    var worked = days.Where(d => d.ActiveHours > 0m).ToList();
    var hours = worked.Select(d => d.ActiveHours).OrderBy(h => h).ToList();

    Console.WriteLine($"days            : {worked.Count} with activity, {weeks.Count} weeks");
    if (hours.Count > 0)
        Console.WriteLine(
            $"daily active    : median {hours[hours.Count / 2]:0.00} h, "
            + $"mean {hours.Average():0.00} h, min {hours[0]:0.00}, max {hours[^1]:0.00}");

    var partial = days.Count(d => d.Quality == DayQuality.Partial);
    var unreliable = days.Count(d => d.Quality == DayQuality.Unreliable);
    Console.WriteLine($"quality         : {days.Count - partial - unreliable} complete, "
        + $"{partial} partial, {unreliable} unreliable");

    if (gaps > 0) Console.WriteLine($"recorder gaps   : {gaps}");
    if (malformed > 0) Console.WriteLine($"malformed lines : {malformed}");

    Console.WriteLine($"written         : {output}");
}
