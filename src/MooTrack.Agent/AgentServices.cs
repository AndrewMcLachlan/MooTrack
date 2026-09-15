using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MooTrack.Agent;

public static class AgentServices
{
    public static IServiceCollection AddAgent(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfigureOptions<AgentOptions>>(
            _ => new ConfigureNamedOptions<AgentOptions>(
                Options.DefaultName, configuration.GetSection(AgentOptions.Section).Bind));

        services.AddSingleton(provider =>
            new ObservationJournal(
                provider.GetRequiredService<IOptions<AgentOptions>>().Value.JournalRoot));

        services.AddSingleton(provider =>
            new PositionStore(
                provider.GetRequiredService<IOptions<AgentOptions>>().Value.BookmarkPath));

        services.AddSingleton<ISignalSource, DesktopMonitor>();
        services.AddSingleton<ISignalSource, EventLogSource>();

        // No collector configured is a valid, useful state: the journal is the record,
        // and shipping back-fills whatever accumulated once a collector does appear.
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<AgentOptions>>().Value;
            if (String.IsNullOrWhiteSpace(settings.CollectorUrl)) return null!;

            var client = new HttpClient
            {
                BaseAddress = new Uri(settings.CollectorUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30),
            };

            return new JournalShipper(
                settings.JournalRoot,
                new PositionStore(settings.ShipmentPath),
                new CollectorClient(
                    client, settings.ApiKey,
                    provider.GetRequiredService<ILogger<CollectorClient>>()),
                settings.ShipBatchSize);
        });

        // An unhandled fault in a background service stops the host by default, which
        // would stop the service. Keep it running; every path logs its own failures.
        services.Configure<HostOptions>(host =>
            host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        services.AddHostedService<Worker>();

        return services;
    }
}
