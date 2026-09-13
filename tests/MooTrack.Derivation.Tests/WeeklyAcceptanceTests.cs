using System.Globalization;
using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class WeeklyAcceptanceTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "data")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void DisplayOnlyModel_ReproducesWeeklyRollup()
    {
        var root = RepoRoot();
        var report = Path.Combine(root, "docs", "data", "raw", "sleepstudy28.html");
        var validation = Path.Combine(root, "docs", "data", "weekly-hours-sleepstudy.csv");
        Assert.True(File.Exists(report), $"acceptance fixture missing: {report}");

        var active = SleepStudyReader.ReadActiveIntervals(report, TimeSpan.Zero);
        var actual = WeeklyReport.Build(DailyReport.Build(active, new DerivationOptions()));

        var expected = File.ReadAllLines(validation)
            .Skip(1)
            .Where(l => l.Length > 0)
            .Select(l => l.Split(','))
            .ToList();

        Assert.Equal(expected.Count, actual.Count);

        var mismatches = expected.Zip(actual)
            .Where(p => p.First[0] != p.Second.IsoWeek
                        || DateOnly.Parse(p.First[1], CultureInfo.InvariantCulture) != p.Second.WeekStart
                        || Decimal.Parse(p.First[2], CultureInfo.InvariantCulture) != p.Second.ActiveHours
                        || Int32.Parse(p.First[3], CultureInfo.InvariantCulture) != p.Second.DaysWithActivity
                        || Decimal.Parse(p.First[4], CultureInfo.InvariantCulture) != p.Second.MeanHoursPerDay
                        || Decimal.Parse(p.First[5], CultureInfo.InvariantCulture) != p.Second.TotalSpanHours)
            .Select(p => $"{p.First[0]}: expected {p.First[2]}h over {p.First[3]}d "
                         + $"mean {p.First[4]} span {p.First[5]} | actual {p.Second.ActiveHours}h "
                         + $"over {p.Second.DaysWithActivity}d mean {p.Second.MeanHoursPerDay} "
                         + $"span {p.Second.TotalSpanHours}")
            .ToList();

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {expected.Count} weeks differ:"
            + Environment.NewLine + String.Join(Environment.NewLine, mismatches));
    }
}
