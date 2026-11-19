using FileVault.Options;
using Microsoft.Extensions.Options;

namespace FileVault.Services;

public sealed class FileProcessorWorker : BackgroundService
{
    private readonly ILogger<FileProcessorWorker> _logger;
    private readonly VaultOptions _options;
    private readonly FileQueueChannel _queue;
    private readonly CryptoCompressionService _crypto;

    public FileProcessorWorker(
        ILogger<FileProcessorWorker> logger,
        IOptions<VaultOptions> options,
        FileQueueChannel queue,
        CryptoCompressionService crypto)
    {
        _logger = logger;
        _options = options.Value;
        _queue = queue;
        _crypto = crypto;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (string sourcePath in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessFileAsync(sourcePath, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file '{SourcePath}'.", sourcePath);
            }
        }
    }

    private async Task ProcessFileAsync(string sourcePath, CancellationToken stoppingToken)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        bool unlocked = await WaitForExclusiveAccessAsync(sourcePath, stoppingToken);
        if (!unlocked)
        {
            _logger.LogWarning(
                "Timed out waiting for exclusive lock on '{SourcePath}' after {MaxRetries} retries.",
                sourcePath,
                _options.MaxLockRetries);
            return;
        }

        string fileName = Path.GetFileName(sourcePath);
        string finalVaultPath = Path.Combine(_options.VaultPath, $"{fileName}.enc");
        string tempVaultPath = $"{finalVaultPath}.tmp";

        try
        {
            await _crypto.EncryptFileAsync(sourcePath, tempVaultPath, stoppingToken);
            File.Move(tempVaultPath, finalVaultPath, overwrite: true);

            _logger.LogInformation(
                "Encrypted '{SourcePath}' -> '{VaultPath}'",
                sourcePath,
                finalVaultPath);
            
            if (_options.DeleteSourceAfterEncrypt)
            {
                File.Delete(sourcePath);
                _logger.LogInformation("Deleted source file '{SourcePath}'.", sourcePath);
            }
        }
        finally
        {
            if (File.Exists(tempVaultPath))
            {
                try
                {
                    File.Delete(tempVaultPath);
                }
                catch
                {
                    // ignore cleanup failure on temp artifact
                }
            }
        }
    }

    private async Task<bool> WaitForExclusiveAccessAsync(string filePath, CancellationToken stoppingToken)
    {
        int delayMs = Math.Max(10, _options.BaseRetryDelayMs);
        int maxDelayMs = Math.Max(1, _options.MaxRetryDelayMs);
        int maxRetries = Math.Max(1, _options.MaxLockRetries);

        for (int attemp = 1; attemp <= maxRetries; attemp++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                await using var probe = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    bufferSize: 1,
                    useAsync: true);
                return true;
            }
            catch (IOException)
            {
                // file is still being written or locked by another handle
            }
            catch (UnauthorizedAccessException)
            {
                // transient access contention while handle closes
            }

            await Task.Delay(delayMs, stoppingToken);
            delayMs = Math.Min(maxDelayMs, delayMs * 2);
        }
        return false;
    }
}