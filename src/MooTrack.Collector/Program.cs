using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MooTrack.Collector;
using MooTrack.Derivation;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("MOOTRACK_");
builder.Services.Configure<CollectorOptions>(
    builder.Configuration.GetSection(CollectorOptions.Section));
builder.Services.PostConfigure<CollectorOptions>(options =>
{
    var key = builder.Configuration["MOOTRACK_API_KEY"] ?? builder.Configuration["ApiKey"];
    if (!String.IsNullOrWhiteSpace(key)) options.ApiKey = key;
});

builder.Services.AddSingleton(provider =>
{
    var settings = provider.GetRequiredService<IOptions<CollectorOptions>>().Value;
    return new RawStore(
        settings.RawRoot,
        String.IsNullOrWhiteSpace(settings.MirrorRoot) ? null : settings.MirrorRoot,
        provider.GetRequiredService<ILogger<RawStore>>());
});
builder.Services.AddSingleton<ReportRegenerator>();

var app = builder.Build();

var configured = app.Services.GetRequiredService<IOptions<CollectorOptions>>().Value;
if (String.IsNullOrWhiteSpace(configured.ApiKey))
{
    app.Logger.LogCritical("MOOTRACK_API_KEY is not set; refusing to start unauthenticated");
    return 1;
}

var probe = app.Services.GetRequiredService<RawStore>().WriteMountProbe();
app.Logger.LogInformation("mount probe written: {Probe}", probe);

app.MapPost("/observations", async (
    HttpRequest request,
    RawStore store,
    ReportRegenerator regenerator,
    IOptions<CollectorOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!Authorised(request, options.Value.ApiKey)) return Results.Unauthorized();

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);

    var log = NdjsonReader.Parse(body.Split('\n'));
    var result = store.Append(log.Observations);

    _ = regenerator.RegenerateAsync(force: false, CancellationToken.None);

    return Results.Ok(new
    {
        accepted = result.Accepted,
        duplicates = result.Duplicates,
        malformed = log.Malformed.Count,
    });
});

app.MapGet("/health", (RawStore store, ReportRegenerator regenerator) =>
{
    var files = Directory.Exists(store.PrimaryRoot)
        ? Directory.GetFiles(store.PrimaryRoot, "*.ndjson", SearchOption.AllDirectories)
        : [];

    return Results.Ok(new
    {
        status = "ok",
        rawRoot = store.PrimaryRoot,
        mirrorRoot = store.MirrorRoot,
        files = files.Length,
        newestObservationFile = files.Length == 0
            ? null
            : files.Select(f => new FileInfo(f).LastWriteTimeUtc).Max().ToString("O"),
        mountProbe = ProbeContent(store),
        lastRegeneration = regenerator.LastRun == DateTimeOffset.MinValue
            ? null
            : regenerator.LastRun.ToString("O"),
    });
});

// Recomputing under different thresholds must never disturb the stored reports:
// this reads the raw store and answers, it does not write.
app.MapGet("/hours", (
    HttpRequest request,
    RawStore store,
    ReportRegenerator regenerator,
    IOptions<CollectorOptions> options,
    int? bridge,
    int? confirm,
    int? gap,
    DateOnly? from,
    DateOnly? to) =>
{
    if (!Authorised(request, options.Value.ApiKey)) return Results.Unauthorized();

    var derivation = regenerator.Derivation with
    {
        BridgeThreshold = bridge is { } b ? TimeSpan.FromMinutes(b) : regenerator.Derivation.BridgeThreshold,
        ConfirmationWindow = confirm is { } c ? TimeSpan.FromMinutes(c) : regenerator.Derivation.ConfirmationWindow,
        GapTolerance = gap is { } g ? TimeSpan.FromMinutes(g) : regenerator.Derivation.GapTolerance,
    };

    var timeline = WorkIntervals.Build(store.Observations(), derivation);
    var days = DailyReport.Build(timeline, derivation)
        .Where(d => (from is null || d.Date >= from) && (to is null || d.Date <= to))
        .ToList();
    var weeks = WeeklyReport.Build(days);

    return Results.Ok(new
    {
        parameters = new
        {
            bridgeMinutes = (int)derivation.BridgeThreshold.TotalMinutes,
            confirmationMinutes = (int)derivation.ConfirmationWindow.TotalMinutes,
            gapToleranceMinutes = (int)derivation.GapTolerance.TotalMinutes,
            fringeMaxMinutes = (int)derivation.FringeMaxDuration.TotalMinutes,
            fringeGapMinutes = (int)derivation.FringeGap.TotalMinutes,
        },
        days = days.Select(d => new
        {
            date = d.Date.ToString("yyyy-MM-dd"),
            weekday = d.Date.DayOfWeek.ToString()[..3],
            firstActive = d.FirstActive?.ToString("HH:mm:ss"),
            lastActive = d.LastActive?.ToString("HH:mm:ss"),
            spanHours = d.SpanHours,
            activeHours = d.ActiveHours,
            awayHours = d.AwayHours,
            sessions = d.Sessions,
            longestBreakMinutes = d.LongestBreakMinutes,
            fringeSessions = d.FringeSessions,
            quality = d.Quality.ToString(),
            unaccountedMinutes = d.UnaccountedMinutes,
        }),
        weeks = weeks.Select(w => new
        {
            isoWeek = w.IsoWeek,
            weekStart = w.WeekStart.ToString("yyyy-MM-dd"),
            activeHours = w.ActiveHours,
            daysWithActivity = w.DaysWithActivity,
            meanHoursPerDay = w.MeanHoursPerDay,
            totalSpanHours = w.TotalSpanHours,
            partialDays = w.PartialDays,
            unreliableDays = w.UnreliableDays,
        }),
    });
});

app.MapPost("/regenerate", async (
    HttpRequest request,
    ReportRegenerator regenerator,
    IOptions<CollectorOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!Authorised(request, options.Value.ApiKey)) return Results.Unauthorized();

    var ran = await regenerator.RegenerateAsync(force: true, cancellationToken);
    return ran ? Results.Ok(new { regenerated = true }) : Results.StatusCode(503);
});

await app.RunAsync();
return 0;

static bool Authorised(HttpRequest request, string expected) =>
    request.Headers.TryGetValue("X-Api-Key", out var provided)
    && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided.ToString()),
        Encoding.UTF8.GetBytes(expected));

static string? ProbeContent(RawStore store)
{
    var path = Path.Combine(store.PrimaryRoot, RawStore.ProbeFileName);
    return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
}

public partial class Program;
