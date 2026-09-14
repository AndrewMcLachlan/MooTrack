using MooTrack.Collector;

namespace MooTrack.Collector.Tests;

public sealed class StorageCheckTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-storage").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void IsWritable_AnOrdinaryDirectory_IsTrue()
    {
        Assert.True(StorageCheck.IsWritable(_root));
    }

    [Fact]
    public void IsWritable_ADirectoryThatDoesNotExistYet_IsTrueAndCreatesIt()
    {
        var path = Path.Combine(_root, "nested", "deeper");

        Assert.True(StorageCheck.IsWritable(path));
        Assert.True(Directory.Exists(path));
    }

    // A file where a directory should be is the shape of a misconfigured mount.
    [Fact]
    public void IsWritable_APathBlockedByAFile_IsFalse()
    {
        var path = Path.Combine(_root, "blocked");
        File.WriteAllText(path, "not a directory");

        Assert.False(StorageCheck.IsWritable(path));
    }

    [Fact]
    public void IsWritable_LeavesNothingBehind()
    {
        StorageCheck.IsWritable(_root);

        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }
}
