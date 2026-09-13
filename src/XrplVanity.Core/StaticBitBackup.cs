using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XrplVanity.Core;

/// <summary>One found wallet, in the shape the StaticBit backup payload wants it.</summary>
public readonly record struct BackupWallet(string Seed, string Address, string? Description);

/// <summary>
/// Builds the backup container the StaticBit wallet's scanner restores from. The format belongs to
/// that application, not to this one: every constant here is copied from it, and the tests pin each
/// one, because nothing warns us when it changes. See
/// docs/2026-09-13-xrpl-vanity-qr-export-design.md.
/// </summary>
public static class StaticBitBackup
{
    /// <summary>What one QR code at correction level H holds: 1852 Base45 characters.</summary>
    public const int MaxQrContainerBytes = 1234;

    private const byte Version = 1;
    private const byte NoPasswordKdfId = 0;
    private const byte Pbkdf2KdfId = 1;
    private const int Iterations = 600_000;
    private const int KeySize = 32;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>magic + version + kdfId — the whole header when there is no password.</summary>
    private const int HeaderPrefixLength = 6;

    /// <summary>prefix + iterations + salt — and the associated data AES-GCM authenticates.</summary>
    private const int PasswordHeaderLength = HeaderPrefixLength + 4 + SaltSize;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>The container as text, ready to draw. The container bytes do not outlive the call.</summary>
    public static string ToQrText(IReadOnlyList<BackupWallet> wallets, string? password)
    {
        byte[] container = Pack(wallets, password);
        try
        {
            return Base45.Encode(container);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(container);
        }
    }

    public static byte[] Pack(IReadOnlyList<BackupWallet> wallets, string? password)
    {
        ArgumentNullException.ThrowIfNull(wallets);
        if (wallets.Count == 0) throw new ArgumentException("A backup needs at least one wallet.", nameof(wallets));

        byte[] compressed = Deflate(Encoding.UTF8.GetBytes(BuildJson(wallets)));
        try
        {
            return password is null ? PackPlain(compressed) : PackEncrypted(compressed, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compressed);
        }
    }

    private static byte[] PackPlain(byte[] compressed)
    {
        byte[] container = new byte[HeaderPrefixLength + compressed.Length];
        WriteHeaderPrefix(container, NoPasswordKdfId);
        compressed.CopyTo(container, HeaderPrefixLength);
        return container;
    }

    private static byte[] PackEncrypted(byte[] compressed, string password)
    {
        byte[] container = new byte[PasswordHeaderLength + NonceSize + compressed.Length + TagSize];
        WriteHeaderPrefix(container, Pbkdf2KdfId);
        BinaryPrimitives.WriteUInt32BigEndian(container.AsSpan(HeaderPrefixLength, 4), Iterations);

        Span<byte> salt = container.AsSpan(HeaderPrefixLength + 4, SaltSize);
        RandomNumberGenerator.Fill(salt);
        Span<byte> nonce = container.AsSpan(PasswordHeaderLength, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Encrypt(
                nonce,
                compressed,
                container.AsSpan(PasswordHeaderLength + NonceSize, compressed.Length),
                container.AsSpan(container.Length - TagSize, TagSize),
                // The whole header, salt included: the wallet authenticates it and so must we.
                container.AsSpan(0, PasswordHeaderLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        return container;
    }

    private static void WriteHeaderPrefix(byte[] container, byte kdfId)
    {
        "SBW1"u8.CopyTo(container);
        container[4] = Version;
        container[5] = kdfId;
    }

    /// <summary>Raw DEFLATE, RFC 1951 — no zlib wrapper, which is what the wallet inflates.</summary>
    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    internal static string BuildJson(IReadOnlyList<BackupWallet> wallets)
    {
        Entry[] entries = new Entry[wallets.Count];
        for (int i = 0; i < wallets.Count; i++)
        {
            BackupWallet wallet = wallets[i];
            entries[i] = new Entry(wallet.Seed, wallet.Address, wallet.Description);
        }

        return JsonSerializer.Serialize(new Payload(entries), SerializerOptions);
    }

    private sealed record Payload([property: JsonPropertyName("wallets")] IReadOnlyList<Entry> Wallets);

    /// <summary>
    /// keyType 1 is the wallet's AccountKeyType.Seed; 0 would mean a watch-only account and the
    /// wallet would refuse to derive from it. algorithm is a free string on that side, and this
    /// generator only ever produces Ed25519 family seeds. The order is stated rather than left to
    /// the serializer's reflection order — the importer reads by name and does not care, but a
    /// stated order is what makes the test a readable claim about the payload.
    /// </summary>
    private sealed record Entry(
        [property: JsonPropertyName("keyValue"), JsonPropertyOrder(1)] string KeyValue,
        [property: JsonPropertyName("classicAddress"), JsonPropertyOrder(2)] string ClassicAddress,
        [property: JsonPropertyName("description"), JsonPropertyOrder(5)] string? Description)
    {
        [JsonPropertyName("keyType"), JsonPropertyOrder(3)] public int KeyType => 1;

        [JsonPropertyName("algorithm"), JsonPropertyOrder(4)] public string Algorithm => "ed25519";
    }
}
