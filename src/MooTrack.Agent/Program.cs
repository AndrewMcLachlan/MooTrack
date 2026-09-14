using MooTrack.Agent;

var builder = Host.CreateApplicationBuilder(args);

// The installed appsettings.json is replaced by an upgrade, which would silently
// discard the collector URL and API key. This copy lives with the data, not the
// binaries, so it survives. Environment variables are re-added afterwards to keep
// them the highest precedence.
builder.Configuration.AddJsonFile(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MooTrack", "appsettings.json"),
    optional: true,
    reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddWindowsService(options => options.ServiceName = "MooTrack");
builder.Services.AddAgent(builder.Configuration);

builder.Services.AddLogging(logging => logging.AddEventLog(settings =>
{
    settings.SourceName = "MooTrack";
    settings.LogName = "Application";
}));

await builder.Build().RunAsync();
