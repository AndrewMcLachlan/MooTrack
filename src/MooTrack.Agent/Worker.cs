using Microsoft.Extensions.Options;
using MooTrack.Derivation;

namespace MooTrack.Agent;

public sealed class Worker(
    IOptions<AgentOptions> options,
    ObservationJournal journal,
    IEnumerable<ISignalSource> sources,
    JournalShipper? shipper,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly AgentOptions _settings = options.Value;
    private readonly ISignalSource[] _signalSources = [.. sources];
    private readonly string _host = Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield before touching anything slow, so nothing on the start path can delay
        // the tick loop behind a large event log.
        await Task.Yield();

        foreach (var source in _signalSources) source.Observed += Record;

        StartSources();

        Record(new SignalObserved(ObservedEvent.AgentStarted, "Reconcile", "agent started"),
            DateTimeOffset.Now);

        var tick = TimeSpan.FromSeconds(Math.Max(1, _settings.TickSeconds));
        var shipEvery = Math.Max(1, _settings.FlushSeconds / Math.Max(1, _settings.TickSeconds));
        var ticks = 0;

        using var timer = new PeriodicTimer(tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;

                Record(new SignalObserved(ObservedEvent.Tick, "Tick", ""), DateTimeOffset.Now);

                if (++ticks % shipEvery == 0) await ShipAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "tick failed; the loop continues");
            }
        }

        Record(new SignalObserved(ObservedEvent.AgentStopped, "Reconcile", "agent stopping"),
            DateTimeOffset.Now);

        await ShipAsync(CancellationToken.None);
    }

    // One source failing to subscribe is not a reason to run without the others.
    private void StartSources()
    {
        foreach (var source in _signalSources)
        {
            var starting = source;
            _ = Task.Run(() =>
            {
                try
                {
                    starting.Start();
                }
                catch (Exception e)
                {
                    logger.LogError(
                        e, "{Source} failed to start; the agent continues without it",
                        starting.GetType().Name);
                }
            });
        }
    }

    private async Task ShipAsync(CancellationToken cancellationToken)
    {
        if (shipper is null) return;

        try
        {
            await shipper.ShipAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "shipping to the collector failed; the journal is unaffected");
        }
    }

    // The journal is the commit point, and the only thing that must not fail.
    private void Record(SignalObserved signal, DateTimeOffset at)
    {
        try
        {
            journal.Append(new Observation
            {
                Timestamp = at,
                UnbiasedMs = MonotonicClock.Milliseconds(),
                Event = signal.Event,
                Host = _host,
                User = _settings.User,
                Source = signal.Source,
                Detail = signal.Detail,
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to record {Event}", signal.Event);
        }
    }

    public override void Dispose()
    {
        foreach (var source in _signalSources)
        {
            try
            {
                source.Dispose();
            }
            catch (Exception e)
            {
                logger.LogError(e, "{Source} failed to dispose", source.GetType().Name);
            }
        }

        base.Dispose();
    }
}
