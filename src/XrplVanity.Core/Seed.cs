using System;
using System.Security.Cryptography;

namespace XrplVanity.Core;

/// <summary>Encodes/decodes the XRPL Ed25519 family seed (sEd...).</summary>
public static class Seed
{
    // Ed25519 seed version prefix: 0x01 0xE1 0x4B.
    private static readonly byte[] Prefix = { 0x01, 0xE1, 0x4B };

    public static string Encode(ReadOnlySpan<byte> entropy16)
    {
        Span<byte> payload = stackalloc byte[23];
        Prefix.CopyTo(payload);
        entropy16.CopyTo(payload[3..]);
        AppendChecksum(payload[..19], payload[19..]);

        Span<char> buffer = stackalloc char[64];
        int len = XrplBase58.Encode(payload, buffer);
        return new string(buffer[..len]);
    }

    public static byte[] DecodeEntropy(string seed)
    {
        byte[] raw = XrplBase58.Decode(seed);
        // raw = 3-byte prefix + 16 entropy + 4 checksum
        byte[] entropy = new byte[16];
        Array.Copy(raw, 3, entropy, 0, 16);
        return entropy;
    }

    internal static void AppendChecksum(ReadOnlySpan<byte> body, Span<byte> checksum4)
    {
        Span<byte> h1 = stackalloc byte[32];
        Span<byte> h2 = stackalloc byte[32];
        SHA256.HashData(body, h1);
        SHA256.HashData(h1, h2);
        h2[..4].CopyTo(checksum4);
    }
}
