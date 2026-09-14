using MooTrack.Collector;

namespace MooTrack.Collector.Tests;

public class MountPointsTests
{
    // Real /proc/self/mountinfo shape: the mount point is the fifth field.
    private const string MountInfo = """
        22 28 0:21 / /proc rw,nosuid,nodev,noexec,relatime - proc proc rw
        28 0 8:1 /var/lib/docker/overlay2/abc/merged / rw,relatime - overlay overlay rw
        39 28 0:34 / /data/raw rw,relatime - ext4 /dev/sda1 rw
        40 28 0:35 / /data/reports rw,relatime - ext4 /dev/sda1 rw
        """;

    [Fact]
    public void Contains_APathThatIsMounted_IsTrue()
    {
        Assert.True(MountPoints.Contains(MountInfo, "/data/raw"));
    }

    [Fact]
    public void Contains_APathThatIsNotMounted_IsFalse()
    {
        Assert.False(MountPoints.Contains(MountInfo, "/data/nowhere"));
    }

    // The failure this exists to catch: the bind mount was never attached, so the
    // path is a perfectly writable directory inside the container's own filesystem.
    [Fact]
    public void Contains_ADirectoryInsideTheContainerFilesystem_IsFalse()
    {
        Assert.False(MountPoints.Contains(MountInfo, "/data"));
    }

    [Fact]
    public void Contains_IsNotFooledByAPrefixMatch()
    {
        Assert.False(MountPoints.Contains(MountInfo, "/data/ra"));
    }

    [Fact]
    public void Contains_IgnoresATrailingSlash()
    {
        Assert.True(MountPoints.Contains(MountInfo, "/data/raw/"));
    }

    [Fact]
    public void Contains_WithNothingMounted_IsFalse()
    {
        Assert.False(MountPoints.Contains("", "/data/raw"));
    }
}
