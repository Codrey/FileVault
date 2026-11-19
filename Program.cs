using FileVault.Options;
using FileVault.Services;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

// bind strongly typed vault configuration
builder.Services
    .AddOptions<VaultOptions>()
    .Bind(builder.Configuration.GetSection(VaultOptions.SectionName))
    .ValidateOnStart();

// register core pipline services
builder.Services.AddSingleton<FileQueueChannel>();
builder.Services.AddSingleton<CryptoCompressionService>();
builder.Services.AddHostedService<DirectoryWatchService>();
builder.Services.AddHostedService<FileProcessorWorker>();

// suppport running both interactiveely in console and as a handless Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FileVault";
});

// route logs to windows event log (application log)
//builder.Logging.Configure<EventLogSettings>(config =>
//{
//    config.SourceName = "FileVault";
//    config.LogName = "Application";
//});

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog( new EventLogSettings
    {
        SourceName = "FileVault",
        LogName = "Application"
    });
}

// builder.Services.AddHostedService<Worker>();
var host = builder.Build();
//host.Run();
await host.RunAsync();