using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MooTrack.Collector.Tests;

public sealed class IngestEndpointTests : IDisposable
{
    private const string Key = "collector-secret";

    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-collector").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public IngestEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MOOTRACK_API_KEY", Key);
            builder.UseSetting("MooTrack:RawRoot", Path.Combine(_root, "raw"));
            builder.UseSetting("MooTrack:ReportRoot", Path.Combine(_root, "reports"));
            builder.UseSetting("MooTrack:RegenerateDebounceSeconds", "0");
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    // The agent records UTC plus the offset it was captured at; these helpers take the
    // LOCAL hour so a fixture reads as the working day it is meant to represent.
    private static string Line(int localHour, int minute, string observed)
    {
        var local = new DateTimeOffset(2026, 9, 14, localHour, minute, 0, TimeSpan.FromHours(10));
        return JsonSerializer.Serialize(new
        {
            tsUtc = local.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            tsOffset = "+10:00",
            unbiasedMs = localHour * 3600000 + minute * 60000,
            host = "WORKSTATION",
            user = "user",
            @event = observed,
            source = "PowerNotify",
            dedupeKey = "x",
            detail = "",
        });
    }

    private HttpClient Client(string? apiKey = Key)
    {
        var client = _factory.CreateClient();
        if (apiKey is not null) client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    private static StringContent Body(params string[] lines) =>
        new(String.Join('\n', lines) + '\n', Encoding.UTF8, "application/x-ndjson");

    [Fact]
    public async Task Post_WithoutApiKey_IsRejected()
    {
        var response = await Client(apiKey: null)
            .PostAsync("/observations", Body(Line(9, 0, "DisplayOn")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithWrongApiKey_IsRejected()
    {
        var response = await Client("not-the-key")
            .PostAsync("/observations", Body(Line(9, 0, "DisplayOn")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidKey_AcceptsAndCountsObservations()
    {
        var response = await Client()
            .PostAsync("/observations", Body(Line(9, 0, "DisplayOn"), Line(9, 1, "UserPresent")));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("accepted").GetInt32());
        Assert.Equal(0, result.GetProperty("duplicates").GetInt32());
    }

    [Fact]
    public async Task Post_SameBatchTwice_IsIdempotent()
    {
        var client = Client();
        await client.PostAsync("/observations", Body(Line(9, 0, "DisplayOn")));

        var response = await client.PostAsync("/observations", Body(Line(9, 0, "DisplayOn")));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, result.GetProperty("accepted").GetInt32());
        Assert.Equal(1, result.GetProperty("duplicates").GetInt32());
    }

    [Fact]
    public async Task Post_WithAMalformedLine_KeepsTheGoodOnesAndReportsTheRest()
    {
        var response = await Client()
            .PostAsync("/observations", Body(Line(9, 0, "DisplayOn"), "{not json"));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("accepted").GetInt32());
        Assert.Equal(1, result.GetProperty("malformed").GetInt32());
    }

    [Fact]
    public async Task Post_WritesRawNdjsonToTheShare()
    {
        await Client().PostAsync("/observations", Body(Line(9, 0, "DisplayOn")));

        var path = Path.Combine(_root, "raw", "WORKSTATION", "2026-09-14.ndjson");
        Assert.True(File.Exists(path), $"expected raw file at {path}");
    }

    [Fact]
    public async Task Health_IsOpenWithoutAKey()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Regenerate_ProducesTheDerivedReports()
    {
        var client = Client();
        await client.PostAsync("/observations", Body(
            Line(9, 0, "DisplayOn"), Line(9, 1, "UserPresent"), Line(17, 0, "DisplayOff")));

        var response = await client.PostAsync("/regenerate", content: null);

        response.EnsureSuccessStatusCode();
        Assert.True(File.Exists(Path.Combine(_root, "reports", "daily-hours.csv")));
        Assert.True(File.Exists(Path.Combine(_root, "reports", "weekly-hours.csv")));
        Assert.True(File.Exists(Path.Combine(_root, "reports", "mootrack-hours.xlsx")));
    }

    [Fact]
    public async Task Regenerate_DerivesHoursFromTheIngestedDay()
    {
        var client = Client();
        await client.PostAsync("/observations", Body(
            Line(9, 0, "DisplayOn"), Line(9, 1, "UserPresent"), Line(17, 0, "DisplayOff")));
        await client.PostAsync("/regenerate", content: null);

        var rows = await File.ReadAllLinesAsync(Path.Combine(_root, "reports", "daily-hours.csv"));

        Assert.Equal(2, rows.Length);
        Assert.StartsWith("2026-09-14,", rows[1]);
        Assert.Contains(",8.00,", rows[1]);
    }
}
