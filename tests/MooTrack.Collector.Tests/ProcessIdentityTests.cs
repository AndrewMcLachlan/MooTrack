using MooTrack.Collector;

namespace MooTrack.Collector.Tests;

public class ProcessIdentityTests
{
    // Real /proc/self/status shape. The effective uid is the second value, which is
    // the one that matters: it is what the kernel checks when the write is attempted.
    private const string Status = """
        Name:	dotnet
        Umask:	0022
        State:	R (running)
        Uid:	2000	2000	2000	2000
        Gid:	2000	2000	2000	2000
        """;

    [Fact]
    public void ParseUid_ReadsTheEffectiveUid()
    {
        Assert.Equal("2000", ProcessIdentity.ParseUid(Status));
    }

    [Fact]
    public void ParseUid_WhenRealAndEffectiveDiffer_PrefersEffective()
    {
        var status = Status.Replace("Uid:\t2000\t2000\t2000\t2000", "Uid:\t0\t1654\t0\t0");

        Assert.Equal("1654", ProcessIdentity.ParseUid(status));
    }

    [Fact]
    public void ParseUid_WithNoUidLine_IsNull()
    {
        Assert.Null(ProcessIdentity.ParseUid("Name:\tdotnet\nState:\tR (running)"));
    }

    [Fact]
    public void ParseUid_WithNothingToRead_IsNull()
    {
        Assert.Null(ProcessIdentity.ParseUid(null));
    }

    [Fact]
    public void EffectiveUser_AlwaysAnswersSomething()
    {
        Assert.False(String.IsNullOrWhiteSpace(ProcessIdentity.EffectiveUser()));
    }
}
