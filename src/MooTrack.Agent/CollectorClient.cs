using System.Net.Http.Headers;
using System.Text;

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
            if (response.IsSuccessStatusCode) return true;

            logger.LogWarning(
                "Collector rejected {Count} observations: {Status} {Reason}. They stay in "
                + "the journal and will be sent again.",
                lines.Count, (int)response.StatusCode, response.StatusCode);
            return false;
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
}
