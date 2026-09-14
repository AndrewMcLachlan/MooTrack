namespace MooTrack.Collector;

public sealed class CollectorOptions
{
    public const string Section = "MooTrack";

    public string RawRoot { get; set; } = "/data/raw";

    public bool RequireMountedRawRoot { get; set; }

    public string ReportRoot { get; set; } = "/data/reports";

    public string ApiKey { get; set; } = "";

    public int RegenerateDebounceSeconds { get; set; } = 60;

    public int BridgeMinutes { get; set; } = 10;

    public int ConfirmationMinutes { get; set; } = 30;

    public int GapToleranceMinutes { get; set; } = 5;
}
