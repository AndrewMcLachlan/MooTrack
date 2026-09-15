using System.Net;
using Microsoft.Extensions.Logging;
using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class CollectorClientLoggingTests
{
    private sealed class Recorder : ILogger
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add($"{logLevel}|{formatter(state, exception)}|{exception?.GetType().Name}");
    }

    private sealed class Stub(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static CollectorClient Build(HttpStatusCode status, Recorder log) =>
        new(new HttpClient(new Stub(status)) { BaseAddress = new Uri("https://collector.local/") },
            "secret", new Logger(log));

    private sealed class Logger(ILogger inner) : ILogger<CollectorClient>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    // Shipping failed silently once already. A collector that cannot be reached must
    // say so: the journal is safe either way, but nobody can tell without a log line.
    [Fact]
    public async Task Send_WhenRejected_SaysSoWithTheStatusCode()
    {
        var log = new Recorder();

        await Build(HttpStatusCode.Unauthorized, log).SendAsync(["x"], CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Contains("Unauthorized", entry);
    }

    [Fact]
    public async Task Send_WhenTransportFails_SaysSoWithTheException()
    {
        var log = new Recorder();
        var client = new CollectorClient(
            new HttpClient(new Stub(HttpStatusCode.OK)), "secret", new Logger(log));

        await client.SendAsync(["x"], CancellationToken.None);

        Assert.Contains(log.Entries, e => e.Contains("InvalidOperationException"));
    }

    [Fact]
    public async Task Send_WhenAccepted_SaysNothing()
    {
        var log = new Recorder();

        await Build(HttpStatusCode.OK, log).SendAsync(["x"], CancellationToken.None);

        Assert.Empty(log.Entries);
    }
}
