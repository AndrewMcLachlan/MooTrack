namespace MooTrack.Collector;

/// <summary>
/// Answers whether a path is an actual mount point.
/// </summary>
/// <remarks>
/// A bind mount that fails to attach is not an error: Docker leaves the container's
/// own directory in its place, writable and empty. The collector then runs perfectly
/// while writing into a filesystem that disappears when the container is recreated.
/// Nothing downstream can tell the difference, so it is checked here, at startup.
/// </remarks>
public static class MountPoints
{
    private const string ProcMountInfo = "/proc/self/mountinfo";

    public static bool IsMounted(string path)
    {
        if (!File.Exists(ProcMountInfo)) return true;

        try
        {
            return Contains(File.ReadAllText(ProcMountInfo), path);
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static bool Contains(string mountInfo, string path)
    {
        var wanted = Normalise(path);

        foreach (var line in mountInfo.Split('\n'))
        {
            var fields = line.Split(' ');
            if (fields.Length > 4 && Normalise(fields[4]) == wanted) return true;
        }

        return false;
    }

    private static string Normalise(string path) =>
        path.Length > 1 ? path.TrimEnd('/') : path;
}
