using System.Globalization;
using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class AcceptanceTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "data")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    static TimeOnly? ToMinute(TimeOnly? value) =>
        value is { } t ? new TimeOnly(t.Hour, t.Minute) : null;

    static string Fixture(string name) => Path.Combine(RepoRoot(), "docs", "data", name);

    sealed record ExpectedDay(
        DateOnly Date, TimeOnly First, TimeOnly Last, decimal Span,
        decimal Active, decimal Away, int Sessions, int LongestBreak, int Fringe);

    static IReadOnlyList<ExpectedDay> ReadValidationSet()
    {
        var path = Fixture("daily-hours.csv");
        Assert.True(File.Exists(path), $"validation set missing: {path}");

        return [.. File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0).Select(line =>
        {
            var f = line.Split(',');
            return new ExpectedDay(
                DateOnly.Parse(f[0], CultureInfo.InvariantCulture),
                TimeOnly.Parse(f[2], CultureInfo.InvariantCulture),
                TimeOnly.Parse(f[3], CultureInfo.InvariantCulture),
                Decimal.Parse(f[4], CultureInfo.InvariantCulture),
                Decimal.Parse(f[5], CultureInfo.InvariantCulture),
                Decimal.Parse(f[6], CultureInfo.InvariantCulture),
                Int32.Parse(f[7], CultureInfo.InvariantCulture),
                (int)Decimal.Parse(f[8], CultureInfo.InvariantCulture),
                Int32.Parse(f[9], CultureInfo.InvariantCulture));
        })];
    }

    [Fact]
    public void DisplayOnlyModel_ReproducesValidationSet()
    {
        var report = Fixture(Path.Combine("raw", "sleepstudy28.html"));
        Assert.True(File.Exists(report), $"acceptance fixture missing: {report}");

        var active = SleepStudyReader.ReadActiveIntervals(report, TimeSpan.Zero);
        var actual = DailyReport.Build(active, new DerivationOptions());
        var expected = ReadValidationSet();

        Assert.Equal(expected.Count, actual.Count);

        var mismatches = expected.Zip(actual)
            .Where(p => p.First.Date != p.Second.Date
                        || p.First.First != ToMinute(p.Second.FirstActive)
                        || p.First.Last != ToMinute(p.Second.LastActive)
                        || p.First.Span != p.Second.SpanHours
                        || p.First.Active != p.Second.ActiveHours
                        || p.First.Away != p.Second.AwayHours
                        || p.First.Sessions != p.Second.Sessions
                        || p.First.LongestBreak != p.Second.LongestBreakMinutes
                        || p.First.Fringe != p.Second.FringeSessions)
            .Select(p => $"{p.First.Date}/{p.Second.Date}: expected {p.First.Active}h away {p.First.Away} span {p.First.Span} "
                         + $"{p.First.First}-{p.First.Last} sessions {p.First.Sessions} "
                         + $"break {p.First.LongestBreak} fringe {p.First.Fringe} | "
                         + $"actual {p.Second.ActiveHours}h away {p.Second.AwayHours} span {p.Second.SpanHours} "
                         + $"{p.Second.FirstActive}-{p.Second.LastActive} sessions {p.Second.Sessions} "
                         + $"break {p.Second.LongestBreakMinutes} fringe {p.Second.FringeSessions}")
            .ToList();

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {expected.Count} days differ:"
            + Environment.NewLine + String.Join(Environment.NewLine, mismatches));
    }
}
