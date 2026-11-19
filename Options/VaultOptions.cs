namespace FileVault.Options;

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public string WatchPath { get; set; } = @"C:\FileVault\Inbox";
    public string VaultPath { get; set; } = @"C:\FileVault\Vault";
    public int MaxLockRetries { get; set; } = 20;
    public int BaseRetryDelayMs { get; set; } = 100;
    public int MaxRetryDelayMs { get; set; } = 2000;
    public bool DeleteSourceAfterEncrypt { get; set; } = false;
    public string KeyStorePath { get; set; } = @"C:\FileVault\master.key.dpapi";
}