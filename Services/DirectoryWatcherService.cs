using FileVault.Options;
using Microsoft.Extensions.Options;

namespace FileVault.Services;

public sealed class DirectoryWatchService : BackgroundService
{
    private readonly ILogger<DirectoryWatchService> _logger;
    private readonly VaultOptions _options;
    private readonly FileQueueChannel _queue;
    private FileSystemWatcher? _watcher;

    public DirectoryWatchService(
        ILogger<DirectoryWatchService> logger,
        IOptions<VaultOptions> options,
        FileQueueChannel queue)
    {
        _logger = logger;
        _options = options.Value;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_options.WatchPath);
        Directory.CreateDirectory(_options.VaultPath);

        _watcher = new FileSystemWatcher(_options.WatchPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnRenamedEvent;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation(
            "Watching directory '{WatchPath}' -> Vault '{VaultPath}'",
            _options.WatchPath,
            _options.VaultPath);

        // seed pre-existing files already sitting in watchpath on startup
        EnqueueExistingFiles();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileEvent;
                _watcher.Renamed -= OnRenamedEvent;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
            }
        }
    }

    private void EnqueueExistingFiles()
    {
        foreach (string path in Directory.EnumerateFiles(_options.WatchPath))
        {
            TryEnqueue(path);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        TryEnqueue(e.FullPath);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        TryEnqueue(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher Encountered an error");
    }

    private void TryEnqueue(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || Directory.Exists(fullPath))
        {
            return;
        }

        string extension = Path.GetExtension(fullPath);
        if (extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".enc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_queue.Writer.TryWrite(fullPath))
        {
            _logger.LogWarning("Failed to enqueue file path: {FilePath}", fullPath);
        }
    }
}