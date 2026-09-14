using System.Net;
using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class CollectorClientTests
{
    private sealed class Stub(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string?> ApiKeys { get; } = [];
        public List<string?> ContentTypes { get; } = [];
        public List<string?> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            ContentTypes.Add(request.Content.Headers.ContentType?.MediaType);
            Paths.Add(request.RequestUri?.AbsolutePath);
            ApiKeys.Add(request.Headers.TryGetValues("X-Api-Key", out var v)
                ? v.FirstOrDefault()
                : null);
            return new HttpResponseMessage(status);
        }
    }

    private static CollectorClient Build(Stub stub, string baseAddress = "https://collector.local/api/") =>
        new(new HttpClient(stub) { BaseAddress = new Uri(baseAddress) }, "secret");

    [Fact]
    public async Task Send_PostsNdjsonToTheObservationsEndpoint()
    {
        var stub = new Stub(HttpStatusCode.OK);

        var sent = await Build(stub).SendAsync(["one", "two"], CancellationToken.None);

        Assert.True(sent);
        Assert.Equal("one\ntwo\n", Assert.Single(stub.Bodies));
        Assert.Equal("application/x-ndjson", Assert.Single(stub.ContentTypes));
        Assert.Equal("/api/observations", Assert.Single(stub.Paths));
    }

    [Fact]
    public async Task Send_IncludesTheApiKeyHeader()
    {
        var stub = new Stub(HttpStatusCode.OK);

        await Build(stub).SendAsync(["one"], CancellationToken.None);

        Assert.Equal(["secret"], stub.ApiKeys);
    }

    [Fact]
    public async Task Send_WhenCollectorRejects_ReportsFailure()
    {
        var sent = await Build(new Stub(HttpStatusCode.Unauthorized))
            .SendAsync(["one"], CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task Send_WhenTransportFails_DoesNotThrow()
    {
        var client = new CollectorClient(new HttpClient(new Stub(HttpStatusCode.OK)), "secret");

        var sent = await client.SendAsync(["one"], CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task Send_WithNothingToSend_SucceedsWithoutCalling()
    {
        var stub = new Stub(HttpStatusCode.InternalServerError);

        var sent = await Build(stub).SendAsync([], CancellationToken.None);

        Assert.True(sent);
        Assert.Empty(stub.Bodies);
    }
}
