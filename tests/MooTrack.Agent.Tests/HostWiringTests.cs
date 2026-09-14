using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class HostWiringTests
{
    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddAgent(builder.Configuration);
        return builder.Build();
    }

    [Fact]
    public void Host_WithNoCollectorConfigured_Resolves()
    {
        using var host = Build(new() { ["MooTrack:CollectorUrl"] = "" });

        Assert.NotEmpty(host.Services.GetServices<IHostedService>());
    }

    [Fact]
    public void Host_WithACollectorConfigured_Resolves()
    {
        using var host = Build(new()
        {
            ["MooTrack:CollectorUrl"] = "https://collector.local/api",
            ["MooTrack:ApiKey"] = "secret",
        });

        Assert.NotNull(host.Services.GetService<JournalShipper>());
    }

    [Fact]
    public void Host_RegistersBothSignalSources()
    {
        using var host = Build(new() { ["MooTrack:CollectorUrl"] = "" });

        Assert.Equal(2, host.Services.GetServices<ISignalSource>().Count());
    }
}
