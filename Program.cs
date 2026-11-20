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

var host = builder.Build();
//host.Run();

// support one shot decryption mode:
// dotnet run -- --decrypt "C:\FileVault\Vault\test-note.txt.enc" "C:\FileVault\Restored\test-note.txt"// builder.Services.AddHostedService<Worker>();
if (args.Length >= 3 && args[0].Equals("--decrypt", StringComparison.OrdinalIgnoreCase))
{
    var crypto = host.Services.GetRequiredService<CryptoCompressionService>();
    await crypto.DecryptFileAsync(args[1], args[2]);
    Console.WriteLine($"Decrypted '{args[1]}' -> '{args[2]}'");
    return;
}

await host.RunAsync();



