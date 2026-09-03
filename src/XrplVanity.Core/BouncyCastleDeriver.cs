using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;

namespace XrplVanity.Core;

/// <summary>
/// Managed Ed25519 address derivation. Holds a reusable RIPEMD-160 instance, so
/// each worker thread must use its own <see cref="BouncyCastleDeriver"/>.
/// </summary>
public sealed class BouncyCastleDeriver : IAddressDeriver
{
    private readonly RipeMD160Digest _ripemd = new();

    public int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest)
    {
        Span<byte> sha512 = stackalloc byte[64];
        SHA512.HashData(entropy16, sha512);

        Span<byte> pub33 = stackalloc byte[33];
        pub33[0] = 0xED;
        Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(sha512[..32], pub33[1..]);

        Span<byte> sha256 = stackalloc byte[32];
        SHA256.HashData(pub33, sha256);

        Span<byte> payload = stackalloc byte[25];
        payload[0] = 0x00; // AccountID version
        _ripemd.BlockUpdate(sha256);
        _ripemd.DoFinal(payload[1..]);

        Seed.AppendChecksum(payload[..21], payload[21..]);

        return XrplBase58.Encode(payload, dest);
    }
}
