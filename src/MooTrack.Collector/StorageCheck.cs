namespace MooTrack.Collector;

/// <summary>
/// Answers whether the collector can actually write where it has been pointed.
/// </summary>
/// <remarks>
/// A directory the container cannot write to is not detectable from a health check
/// or from any amount of configuration review: the service starts, reports healthy,
/// and fails every ingest. Proving it at startup turns that into one loud failure.
/// </remarks>
public static class StorageCheck
{
    public static bool IsWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Mirror what ingest does: a per-host subdirectory, then a file inside it.
            // Probing only the top level would pass on a directory that permits writes
            // but not mkdir, and the first observation would still be rejected.
            var probe = Path.Combine(path, $".mootrack-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(probe);
            File.WriteAllText(Path.Combine(probe, "write"), String.Empty);
            Directory.Delete(probe, recursive: true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
