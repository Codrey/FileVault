# FileVault

With the current required focus on technology security FileVault was a fun and interesting use case for the `C# Programming Language`, the project was built with **.NET 10 (`net10.0-windows`)** as a Windows Worker Service that watches and monitors an inbox folder, compresses the files with **Brotli**, encrypts them using **AES-256-GCM**, stores a master key using **Windows DPAPI** and supports one shot simple CLI decryption(as detailed below).

## How It Works

1. **Directory Monitoring (`DirectoryWatcherService`)**: Watches `WatchPath` a specific directory (`C:\FileVault\Inbox` by default) via `FileSystemWatcher`, skips `.tmp` and `.enc` files, It will seed any pre existing files on startup and pushes candidate file paths into a `Channel<string>` queue.

2. **Lock Backoff & Processing (`FileProcessorWorker`)**: Will consumes file paths from the channel and it probes for exclusive read access with exponential backoff (`BaseRetryDelayMs` up to `MaxRetryDelayMs`, capped at `MaxLockRetries`), writes to the `<filename>.enc.tmp` in `VaultPath`, and also atomically renames the file to `<filename>.enc`. FileVault has the option of deleting the source file when `DeleteSourceAfterEncrypt` is `true`.

3. **Compression, Encryption & Key Storage (`CryptoCompressionService`)**:
   - Compresses the file contents with `BrotliStream` (`CompressionLevel.Optimal`).
   - Encrypts that compressed date with `AesGcm` using a fresh 12-byte nonce and 16-byte authentication tag.
   - FileVault also writes a `BVAULT` binary container: `[6B "BVAULT"][1B Version][12B Nonce][16B Tag][Ciphertext]`.
   - To Protect the date it loads a 256-bit master key with Windows DPAPI (`ProtectedData`).

## Configuration (`appsettings.json`)

```json
{
  "Vault": {
    "WatchPath": "C:\\FileVault\\Inbox",
    "VaultPath": "C:\\FileVault\\Vault",
    "MaxLockRetries": 20,
    "BaseRetryDelayMs": 100,
    "MaxRetryDelayMs": 2000,
    "DeleteSourceAfterEncrypt": false,
    "KeyStorePath": "C:\\FileVault\\master.key.dpapi"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

## Build & Run

From the project directory:

```powershell
dotnet build
dotnet run
```

Drop a test file into `C:\FileVault\Inbox` to verify it is written as `<filename>.enc` in `C:\FileVault\Vault`.

### Decrypting a Vaulted File

```powershell
dotnet run -- --decrypt "C:\FileVault\Vault\test-note.txt.enc" "C:\FileVault\Restored\test-note.txt"
```

## Project Structure

- `Program.cs` — Host builder, options binding, dependency injection, Windows Service / Event Log setup, and `--decrypt` CLI handling.
- `Options/VaultOptions.cs` — Strongly typed configuration model bound to the `"Vault"` section.
- `Services/FileQueueChannel.cs` — `System.Threading.Channels` wrapper decoupling file discovery from processing.
- `Services/DirectoryWatcherService.cs` — `BackgroundService` wrapping `FileSystemWatcher`.
- `Services/FileProcessorWorker.cs` — `BackgroundService` handling file lock retries, atomic output writes, and optional source cleanup.
- `Services/CryptoCompressionService.cs` — Brotli compression/decompression, AES-256-GCM authenticated encryption/decryption, and DPAPI key management.

## Core Concepts & Reference Links

- **C# Program Structure & Top-Level Statements:** How `Program.cs`, namespaces, and project structure fit together — [General structure of a C# program](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/) and [Top-level statements](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements).
- **Authenticated Encryption (`AesGcm`):** Symmetric encryption with a per-file nonce and authentication tag to detect tampering — [AesGcm Class](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0).
- **Stream Compression (`BrotliStream`):** Compressing file bytes before encryption to reduce payload size — [BrotliStream Class](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream?view=net-11.0).
- **Windows Data Protection API (`ProtectedData`):** OS-managed key protection tied to Windows user or machine credentials — [ProtectedData Class](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=windowsdesktop-11.0).
- **Directory Monitoring (`FileSystemWatcher`):** Listening for file creation and rename events in a watched folder — [FileSystemWatcher Class](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.filter?view=net-10.0).