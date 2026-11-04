using Org.BouncyCastle.Crypto.Digests;
using XrplVanity.Core;

namespace XrplVanity.MobileBench;

/// <summary>
/// <see cref="BouncyCastleDeriver"/> with every hash done in managed code. On Android the
/// <c>System.Security.Cryptography</c> one-shot hashes cross into the platform's crypto library,
/// and a candidate makes four of those calls on inputs of 16 to 33 bytes, so the crossing rather
/// than the hashing may be what they cost. Running both derivers prices that.
/// </summary>
internal sealed class ManagedDigestDeriver : IAddressDeriver
{
    private readonly Sha512Digest _sha512 = new();
    private readonly Sha256Digest _sha256 = new();
    private readonly RipeMD160Digest _ripemd = new();

    public int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest)
    {
        Span<byte> sha512 = stackalloc byte[64];
        _sha512.BlockUpdate(entropy16);
        _sha512.DoFinal(sha512);

        Span<byte> pub33 = stackalloc byte[33];
        pub33[0] = 0xED;
        Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(sha512[..32], pub33[1..]);

        Span<byte> sha256 = stackalloc byte[32];
        _sha256.BlockUpdate(pub33);
        _sha256.DoFinal(sha256);

        Span<byte> payload = stackalloc byte[25];
        payload[0] = 0x00; // AccountID version
        _ripemd.BlockUpdate(sha256);
        _ripemd.DoFinal(payload[1..]);

        _sha256.BlockUpdate(payload[..21]);
        _sha256.DoFinal(sha256);
        _sha256.BlockUpdate(sha256);
        _sha256.DoFinal(sha256);
        sha256[..4].CopyTo(payload[21..]);

        return XrplBase58.Encode(payload, dest);
    }
}
