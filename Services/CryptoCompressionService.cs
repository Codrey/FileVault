using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileVault.Options;
using Microsoft.Extensions.Options;

namespace FileVault.Services;

public sealed class CryptoCompressionService
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("BVAULT");
    private const byte FormatVersion = 1;
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12; // Standard 96-bit GCM nonce
    private const int TagSizeBytes = 16;   // 128-bit GCM authentication tag

    private readonly byte[] _masterKey;

    public CryptoCompressionService(IOptions<VaultOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _masterKey = LoadOrCreateMasterKey(options.Value.KeyStorePath);
    }

    /// <summary>
    /// Compresses a file with Brotli, encrypts with AES-256-GCM, and writes the BVAULT container.
    /// </summary>
    public async Task EncryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        string? destDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // 1. Compress source data into memory/stream using Brotli
        byte[] compressedPayload = await CompressFileAsync(sourceFilePath, cancellationToken);

        // 2. Generate unique per-file nonce and allocate tag + ciphertext buffers
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[compressedPayload.Length];

        // 3. Authenticate + encrypt with AES-256-GCM
        using var aesGcm = new AesGcm(_masterKey, TagSizeBytes);
        aesGcm.Encrypt(nonce, compressedPayload, ciphertext, tag);

        // 4. Write deterministic BVAULT container
        await using var output = new FileStream(
            destinationFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65536,
            useAsync: true);

        await output.WriteAsync(MagicBytes, cancellationToken);
        output.WriteByte(FormatVersion);
        await output.WriteAsync(nonce, cancellationToken);
        await output.WriteAsync(tag, cancellationToken);
        await output.WriteAsync(ciphertext, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies and decrypts a BVAULT container with AES-256-GCM, then decompresses Brotli data.
    /// </summary>
    public async Task DecryptFileAsync(
        string encryptedFilePath,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        byte[] fileBytes = await File.ReadAllBytesAsync(encryptedFilePath, cancellationToken);
        int headerLength = MagicBytes.Length + 1 + NonceSizeBytes + TagSizeBytes;

        if (fileBytes.Length < headerLength)
        {
            throw new InvalidDataException("Encrypted file is truncated or invalid.");
        }

        ReadOnlySpan<byte> span = fileBytes;
        int offset = 0;

        if (!span.Slice(offset, MagicBytes.Length).SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid BVAULT magic header.");
        }
        offset += MagicBytes.Length;

        byte version = span[offset++];
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported BVAULT container version: {version}.");
        }

        byte[] nonce = span.Slice(offset, NonceSizeBytes).ToArray();
        offset += NonceSizeBytes;

        byte[] tag = span.Slice(offset, TagSizeBytes).ToArray();
        offset += TagSizeBytes;

        byte[] ciphertext = span[offset..].ToArray();
        byte[] compressedPayload = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_masterKey, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, compressedPayload);

        await DecompressToFileAsync(compressedPayload, outputFilePath, cancellationToken);
    }

    private static async Task<byte[]> CompressFileAsync(
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            sourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            useAsync: true);

        using var compressedStream = new MemoryStream();
        await using (var brotli = new BrotliStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            await input.CopyToAsync(brotli, cancellationToken);
        }

        return compressedStream.ToArray();
    }

    private static async Task DecompressToFileAsync(
        byte[] compressedData,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        string? outDir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using var input = new MemoryStream(compressedData);
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        await using var output = new FileStream(
            outputFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65536,
            useAsync: true);

        await brotli.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static byte[] LoadOrCreateMasterKey(string keyStorePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyStorePath);

        if (File.Exists(keyStorePath))
        {
            byte[] protectedKey = File.ReadAllBytes(keyStorePath);
            return ProtectedData.Unprotect(
                protectedKey,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
        }

        string? keyDir = Path.GetDirectoryName(keyStorePath);
        if (!string.IsNullOrWhiteSpace(keyDir))
        {
            Directory.CreateDirectory(keyDir);
        }

        byte[] rawKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
        byte[] protectedKeyBlob = ProtectedData.Protect(
            rawKey,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        File.WriteAllBytes(keyStorePath, protectedKeyBlob);
        return rawKey;
    }
}