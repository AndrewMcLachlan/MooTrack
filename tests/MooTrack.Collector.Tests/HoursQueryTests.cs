using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MooTrack.Collector.Tests;

public sealed class HoursQueryTests : IAsyncLifetime, IDisposable
{
    private const string Key = "collector-secret";

    private readonly string _root = Directory.CreateTempSubdirectory("mootrack-hours").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public HoursQueryTests()
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

    private static string Line(int day, int localHour, int minute, string observed)
    {
        var local = new DateTimeOffset(2026, 9, day, localHour, minute, 0, TimeSpan.FromHours(10));
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

    // An eight minute gap: bridged at the ten minute default, a break at five.
    public async Task InitializeAsync()
    {
        var lines = new[]
        {
            Line(14, 9, 0, "DisplayOn"),
            Line(14, 9, 1, "UserPresent"),
            Line(14, 10, 0, "UserInactive"),
            Line(14, 10, 8, "UserPresent"),
            Line(14, 17, 0, "DisplayOff"),
            Line(15, 9, 0, "DisplayOn"),
            Line(15, 9, 1, "UserPresent"),
            Line(15, 16, 0, "DisplayOff"),
        };

        await Client().PostAsync("/observations",
            new StringContent(String.Join('\n', lines) + '\n', Encoding.UTF8, "application/x-ndjson"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Hours(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/hours{query}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Hours_WithoutApiKey_IsRejected()
    {
        var response = await Client(apiKey: null).GetAsync("/hours");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hours_ReturnsADayPerRecordedDay()
    {
        var result = await Hours(Client(), "");

        Assert.Equal(2, result.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task Hours_AtTheDefaultBridge_CountsTheShortGapAsWork()
    {
        var result = await Hours(Client(), "");

        var first = result.GetProperty("days")[0];
        Assert.Equal("2026-09-14", first.GetProperty("date").GetString());
        Assert.Equal(8.00m, first.GetProperty("activeHours").GetDecimal());
    }

    [Fact]
    public async Task Hours_AtATighterBridge_ExcludesTheSameGap()
    {
        var result = await Hours(Client(), "?bridge=5");

        var first = result.GetProperty("days")[0];
        Assert.Equal(7.87m, first.GetProperty("activeHours").GetDecimal());
    }

    [Fact]
    public async Task Hours_EchoesTheParametersItUsed()
    {
        var result = await Hours(Client(), "?bridge=5");

        Assert.Equal(5, result.GetProperty("parameters").GetProperty("bridgeMinutes").GetInt32());
    }

    [Fact]
    public async Task Hours_FilteredByDate_ReturnsOnlyThatRange()
    {
        var result = await Hours(Client(), "?from=2026-09-15");

        var days = result.GetProperty("days");
        Assert.Equal(1, days.GetArrayLength());
        Assert.Equal("2026-09-15", days[0].GetProperty("date").GetString());
    }

    [Fact]
    public async Task Hours_IncludesTheWeeklyRollup()
    {
        var result = await Hours(Client(), "");

        var weeks = result.GetProperty("weeks");
        Assert.Equal(1, weeks.GetArrayLength());
        Assert.Equal(15.00m, weeks[0].GetProperty("activeHours").GetDecimal());
    }

    [Fact]
    public async Task Hours_DoesNotRewriteTheStoredReports()
    {
        var reportPath = Path.Combine(_root, "reports", "daily-hours.csv");
        var before = File.Exists(reportPath) ? File.GetLastWriteTimeUtc(reportPath) : DateTime.MinValue;

        await Hours(Client(), "?bridge=1");

        var after = File.Exists(reportPath) ? File.GetLastWriteTimeUtc(reportPath) : DateTime.MinValue;
        Assert.Equal(before, after);
    }
}
