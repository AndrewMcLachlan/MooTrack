namespace MooTrack.Collector;

/// <summary>
/// The user id the process is actually running as.
/// </summary>
/// <remarks>
/// The image's APP_UID is not the answer: a deployment can override the user, and a
/// message naming the wrong uid sends someone to chown a directory to an account
/// that was never going to write to it.
/// </remarks>
public static class ProcessIdentity
{
    public static string EffectiveUser() =>
        ParseUid(ReadStatus())
        ?? Environment.GetEnvironmentVariable("APP_UID")
        ?? "the container user";

    public static string? ParseUid(string? procStatus)
    {
        if (procStatus is null) return null;

        foreach (var line in procStatus.Split('\n'))
        {
            if (!line.StartsWith("Uid:", StringComparison.Ordinal)) continue;

            // Uid: <real> <effective> <saved> <filesystem>
            var fields = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 2) return fields[2];
        }

        return null;
    }

    private static string? ReadStatus()
    {
        try
        {
            return File.Exists("/proc/self/status") ? File.ReadAllText("/proc/self/status") : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
