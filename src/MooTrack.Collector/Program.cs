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
    return new RawStore(settings.RawRoot);
});
builder.Services.AddSingleton<ReportRegenerator>();

var app = builder.Build();

var configured = app.Services.GetRequiredService<IOptions<CollectorOptions>>().Value;
if (String.IsNullOrWhiteSpace(configured.ApiKey))
{
    app.Logger.LogCritical("MOOTRACK_API_KEY is not set; refusing to start unauthenticated");
    return 1;
}

// A bind mount that failed to attach leaves a writable directory in its place, so
// the collector would run for weeks and lose everything when the container is next
// recreated. Refuse to start instead.
if (configured.RequireMountedRawRoot && !MountPoints.IsMounted(configured.RawRoot))
{
    app.Logger.LogCritical(
        "{Path} is not a mount point. The volume did not attach; anything written "
        + "there would be lost when this container is recreated.", configured.RawRoot);
    return 1;
}

// Unwritable storage is invisible to a health check: the collector starts, reports
// healthy, and fails every ingest. Prove it here instead.
var uid = Environment.GetEnvironmentVariable("APP_UID") ?? "the container user";

if (!StorageCheck.IsWritable(configured.RawRoot))
{
    app.Logger.LogCritical(
        "Cannot write to {Path} as uid {Uid}. Give the mounted directory to that "
        + "user on the host and start again: {Remedy}",
        configured.RawRoot, uid, $"chown -R {uid}:{uid} <host path>");
    return 1;
}

// Reports are derived and can be rebuilt, so an unwritable report directory is worth
// shouting about but not worth refusing the observations that are still arriving.
if (!StorageCheck.IsWritable(configured.ReportRoot))
{
    app.Logger.LogCritical(
        "Cannot write to {Path}; observations will be stored but no reports produced.",
        configured.ReportRoot);
}

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
    var files = Directory.Exists(store.Root)
        ? Directory.GetFiles(store.Root, "*.ndjson", SearchOption.AllDirectories)
        : [];

    return Results.Ok(new
    {
        status = "ok",
        rawRoot = store.Root,
        files = files.Length,
        newestObservationFile = files.Length == 0
            ? null
            : files.Select(f => new FileInfo(f).LastWriteTimeUtc).Max().ToString("O"),
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

public partial class Program;
