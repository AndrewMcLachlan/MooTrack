namespace MooTrack.Derivation.Tests;

/// <summary>
/// Locates the recorded working-hours data the acceptance tests derive against.
/// That data describes a real person's movements, so it is kept out of the
/// repository: it lives on the machine that produced it and nowhere else.
/// </summary>
public static class ValidationData
{
    public static string? Root { get; } = Locate();

    public static bool Available => Root is not null;

    public const string Absent =
        "recorded working-hours data is not in the repository; see docs/data/README.md";

    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([Root ?? throw new InvalidOperationException(Absent), .. parts]);

    private static string? Locate()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("MOOTRACK_VALIDATION_DATA");
        if (!String.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
            return fromEnvironment;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "docs", "data");
            if (File.Exists(System.IO.Path.Combine(candidate, "daily-hours.csv"))) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// A fact that needs the recorded working-hours data. Skipped, not failed, when that
/// data is absent — a clean checkout legitimately does not have it.
/// </summary>
public sealed class ValidationDataFactAttribute : FactAttribute
{
    public ValidationDataFactAttribute()
    {
        if (!ValidationData.Available) Skip = ValidationData.Absent;
    }
}
