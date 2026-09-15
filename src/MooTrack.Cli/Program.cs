using MooTrack.Cli;
using MooTrack.Derivation;
using MooTrack.Reporting;

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
      --restart-allowance <min>  outage still counted as work (default 30)
      --bridge-across-lock   treat an explicit lock as bridgeable
    """;

try
{
    var arguments = Arguments.Parse(args);
    Directory.CreateDirectory(arguments.Output);

    var days = Derive(arguments, out var malformed, out var gaps, out var timeline);
    var weeks = WeeklyReport.Build(days);

    ReportWriter.WriteAll(
        arguments.Output, days, weeks, arguments.Options,
        onWorkbookFailure: e => Console.Error.WriteLine(e.Message));

    ReportWriter.WriteCsv(
        Path.Combine(arguments.Output, "timeline.csv"),
        CsvFormat.TimelineHeader,
        timeline.Select(CsvFormat.Row));

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
    Arguments arguments, out int malformed, out int gaps, out IReadOnlyList<TimelineEntry> timeline)
{
    if (arguments.SleepStudy is { } report)
    {
        malformed = 0;
        gaps = 0;
        var intervals = SleepStudyReader.ReadActiveIntervals(report, arguments.Offset);
        timeline = DayTimeline.Build(intervals, []);
        return DailyReport.Build(intervals, arguments.Options);
    }

    var log = ReadObservations(arguments.Ndjson!);
    var observed = WorkIntervals.Build(log.Observations, arguments.Options);

    malformed = log.Malformed.Count;
    gaps = observed.Gaps.Count;
    timeline = DayTimeline.Build(observed.Active, observed.Gaps);
    return DailyReport.Build(observed, arguments.Options);
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
