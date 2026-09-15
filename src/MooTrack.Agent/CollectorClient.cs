using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MooTrack.Agent;

public sealed class CollectorClient(
    HttpClient client, string apiKey, ILogger<CollectorClient> logger) : ICollectorClient
{
    public async Task<bool> SendAsync(
        IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) return true;

        try
        {
            var body = new StringBuilder();
            foreach (var line in lines) body.Append(line).Append('\n');

            using var content = new StringContent(
                body.ToString(), Encoding.UTF8, "application/x-ndjson");
            using var request = new HttpRequestMessage(HttpMethod.Post, "observations")
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Collector rejected {Count} observations: {Status} {Reason}. They stay in "
                    + "the journal and will be sent again.",
                    lines.Count, (int)response.StatusCode, response.StatusCode);
                return false;
            }

            return await Accounted(response, lines.Count, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException
                                      or TaskCanceledException or UriFormatException)
        {
            logger.LogWarning(
                e, "Collector unreachable at {Uri}; {Count} observations stay in the journal.",
                client.BaseAddress, lines.Count);
            return false;
        }
    }

    /// <summary>
    /// A success status says the request was handled, not that the observations were
    /// stored. Advancing on the status alone once skipped a whole day: the collector
    /// answered 200 having kept none of them.
    /// </summary>
    private async Task<bool> Accounted(
        HttpResponseMessage response, int sent, CancellationToken cancellationToken)
    {
        int accepted, duplicates, malformed;

        try
        {
            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            accepted = Count(body.RootElement, "accepted");
            duplicates = Count(body.RootElement, "duplicates");
            malformed = Count(body.RootElement, "malformed");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return true;
        }

        if (accepted + duplicates == sent) return true;

        logger.LogError(
            "Collector stored {Accepted} and already held {Duplicates} of {Sent} observations, "
            + "rejecting {Malformed} as unreadable. They stay in the journal and will be sent "
            + "again; nothing is lost, but nothing is arriving either.",
            accepted, duplicates, sent, malformed);

        return false;
    }

    private static int Count(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;
}
