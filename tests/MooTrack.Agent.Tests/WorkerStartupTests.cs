using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MooTrack.Agent;
using MooTrack.Derivation;

namespace MooTrack.Agent.Tests;

public sealed class WorkerStartupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-worker").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class SlowSource(TimeSpan delay) : ISignalSource
    {
        public bool Started { get; private set; }

        public event Action<SignalObserved, DateTimeOffset>? Observed;

        public void Start()
        {
            Thread.Sleep(delay);
            Started = true;
            Observed?.Invoke(new SignalObserved(ObservedEvent.DisplayOn, "Fake", "on"),
                DateTimeOffset.Now);
        }

        public void Dispose() { }
    }

    private sealed class ThrowingSource : ISignalSource
    {
        public event Action<SignalObserved, DateTimeOffset>? Observed
        {
            add { }
            remove { }
        }

        public void Start() => throw new InvalidOperationException("subscription refused");

        public void Dispose() { }
    }

    private Worker Build(params ISignalSource[] sources) =>
        new(Options.Create(new AgentOptions { JournalRoot = _root, TickSeconds = 1 }),
            new ObservationJournal(_root),
            sources,
            shipper: null,
            NullLogger<Worker>.Instance);

    [Fact]
    public async Task Ticks_StartOnTime_EvenWhileASourceIsStillStarting()
    {
        var slow = new SlowSource(TimeSpan.FromSeconds(4));
        var worker = Build(slow);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        var log = NdjsonReader.Parse(
            Directory.GetFiles(_root, "*.ndjson").SelectMany(File.ReadAllLines));

        Assert.False(slow.Started, "the slow source should still be starting");
        Assert.Contains(log.Observations, o => o.Event == ObservedEvent.Tick);
    }

    [Fact]
    public async Task StartAsync_EventuallyStartsTheSource()
    {
        var slow = new SlowSource(TimeSpan.Zero);
        var worker = Build(slow);

        await worker.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!slow.Started && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.True(slow.Started);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenASourceThrows_TheServiceKeepsRunning()
    {
        var healthy = new SlowSource(TimeSpan.Zero);
        var worker = Build(new ThrowingSource(), healthy);

        await worker.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!healthy.Started && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.True(healthy.Started, "a failing source must not prevent the others starting");
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ticks_AreJournalledWhileRunning()
    {
        var worker = Build(new SlowSource(TimeSpan.Zero));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await worker.StopAsync(CancellationToken.None);

        var written = Directory.GetFiles(_root, "*.ndjson")
            .SelectMany(File.ReadAllLines)
            .ToList();

        var log = NdjsonReader.Parse(written);
        Assert.Empty(log.Malformed);
        Assert.Contains(log.Observations, o => o.Event == ObservedEvent.Tick);
    }
}
