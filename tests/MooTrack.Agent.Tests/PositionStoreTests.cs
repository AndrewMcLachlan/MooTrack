using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public sealed class PositionStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-bookmark").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Path() => System.IO.Path.Combine(_root, "bookmarks.json");

    [Fact]
    public void Load_WithNoFile_StartsFromNothing()
    {
        var store = new PositionStore(Path());

        Assert.Null(store.LastRecordId("Security"));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsPerLog()
    {
        new PositionStore(Path()).Save("Security", 4321);

        var reopened = new PositionStore(Path());

        Assert.Equal(4321, reopened.LastRecordId("Security"));
        Assert.Null(reopened.LastRecordId("System"));
    }

    [Fact]
    public void Save_ForASecondLog_KeepsTheFirst()
    {
        var store = new PositionStore(Path());

        store.Save("Security", 10);
        store.Save("System", 20);

        var reopened = new PositionStore(Path());
        Assert.Equal(10, reopened.LastRecordId("Security"));
        Assert.Equal(20, reopened.LastRecordId("System"));
    }

    [Fact]
    public void Load_WithATornFile_ReplaysRatherThanThrowing()
    {
        File.WriteAllText(Path(), "{\"Security\": 42");

        var store = new PositionStore(Path());

        Assert.Null(store.LastRecordId("Security"));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        new PositionStore(Path()).Save("Security", 7);

        Assert.Equal(["bookmarks.json"], Directory.GetFiles(_root).Select(System.IO.Path.GetFileName));
    }
}
