using Microsoft.Extensions.Options;
using MooTrack.Derivation;
using MooTrack.Reporting;

namespace MooTrack.Collector;

/// <summary>
/// Rebuilds the derived reports from the raw store. Derivation happens here and only
/// here, so hours can be recomputed under different parameters without re-recording.
/// </summary>
public sealed class ReportRegenerator(
    RawStore store,
    IOptions<CollectorOptions> options,
    ILogger<ReportRegenerator> logger)
{
    private readonly CollectorOptions _settings = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastRun = DateTimeOffset.MinValue;

    public DateTimeOffset LastRun => _lastRun;

    public DerivationOptions Derivation => new()
    {
        BridgeThreshold = TimeSpan.FromMinutes(_settings.BridgeMinutes),
        ConfirmationWindow = TimeSpan.FromMinutes(_settings.ConfirmationMinutes),
        GapTolerance = TimeSpan.FromMinutes(_settings.GapToleranceMinutes),
    };

    public async Task<bool> RegenerateAsync(bool force, CancellationToken cancellationToken)
    {
        var debounce = TimeSpan.FromSeconds(Math.Max(0, _settings.RegenerateDebounceSeconds));
        if (!force && DateTimeOffset.UtcNow - _lastRun < debounce) return false;

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)) return false;

        try
        {
            var options = Derivation;
            var timeline = WorkIntervals.Build(store.Observations(), options);
            var days = DailyReport.Build(timeline, options);
            var weeks = WeeklyReport.Build(days);

            ReportWriter.WriteAll(_settings.ReportRoot, days, weeks, options, onWorkbookFailure: e => logger.LogWarning("workbook not rewritten: {Reason}", e.Message));

            _lastRun = DateTimeOffset.UtcNow;
            logger.LogInformation("regenerated {Days} days across {Weeks} weeks", days.Count, weeks.Count);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "report regeneration failed; the raw store is unaffected");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}
