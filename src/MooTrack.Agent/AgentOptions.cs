namespace MooTrack.Agent;

public sealed class AgentOptions
{
    public const string Section = "MooTrack";

    private static string Data(string name) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MooTrack", name);

    public string JournalRoot { get; set; } = Data("journal");

    public string BookmarkPath { get; set; } = Data("bookmarks.json");

    public string ShipmentPath { get; set; } = Data("shipped.json");

    public string? CollectorUrl { get; set; }

    public string ApiKey { get; set; } = "";

    public string User { get; set; } = "";

    public int TickSeconds { get; set; } = 60;

    public int FlushSeconds { get; set; } = 300;

    public int ShipBatchSize { get; set; } = 500;
}
