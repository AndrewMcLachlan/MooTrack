using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class CollectorAcceptanceTests
{
    private sealed class Stub(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static CollectorClient Build(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new Stub(json, status)) { BaseAddress = new Uri("https://c.local/") },
            "secret", NullLogger<CollectorClient>.Instance);

    private static readonly string[] Three = ["a", "b", "c"];

    [Fact]
    public async Task Send_WhenAllAccepted_ReportsSuccess()
    {
        var sent = await Build("""{"accepted":3,"duplicates":0,"malformed":0}""")
            .SendAsync(Three, CancellationToken.None);

        Assert.True(sent);
    }

    [Fact]
    public async Task Send_WhenAlreadyHeld_CountsDuplicatesAsAccounted()
    {
        var sent = await Build("""{"accepted":0,"duplicates":3,"malformed":0}""")
            .SendAsync(Three, CancellationToken.None);

        Assert.True(sent);
    }

    // The failure that lost a day: a 200 carrying "we stored none of them". Advancing
    // on the status code alone skips the batch forever.
    [Fact]
    public async Task Send_WhenNoneWereStored_DoesNotReportSuccess()
    {
        var sent = await Build("""{"accepted":0,"duplicates":0,"malformed":3}""")
            .SendAsync(Three, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task Send_WhenSomeWereDropped_DoesNotReportSuccess()
    {
        var sent = await Build("""{"accepted":2,"duplicates":0,"malformed":1}""")
            .SendAsync(Three, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task Send_WhenFewerAreAccountedForThanSent_DoesNotReportSuccess()
    {
        var sent = await Build("""{"accepted":1,"duplicates":0,"malformed":0}""")
            .SendAsync(Three, CancellationToken.None);

        Assert.False(sent);
    }

    // A collector that answers with something else is still a collector that said OK.
    [Fact]
    public async Task Send_WhenTheBodyCannotBeRead_TrustsTheStatusCode()
    {
        var sent = await Build("not json").SendAsync(Three, CancellationToken.None);

        Assert.True(sent);
    }
}
