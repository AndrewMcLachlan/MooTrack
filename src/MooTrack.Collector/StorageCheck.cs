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

            var probe = Path.Combine(path, $".mootrack-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, String.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
