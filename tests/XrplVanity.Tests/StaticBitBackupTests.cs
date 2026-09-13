using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class StaticBitBackupTests
{
    private static readonly BackupWallet One =
        new("sEdTM1uX8pu2do5XvTnutH6HsouMaM2", "rLczgQHxPhWtjkaQqn3Q6UM8AbRbbRvs5K", "static..bit");

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using MemoryStream input = new(compressed.ToArray());
        using DeflateStream deflate = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void The_Json_Is_Exactly_What_The_Wallet_Requires()
    {
        Assert.Equal(
            "{\"wallets\":[{\"keyValue\":\"sEdTM1uX8pu2do5XvTnutH6HsouMaM2\"," +
            "\"classicAddress\":\"rLczgQHxPhWtjkaQqn3Q6UM8AbRbbRvs5K\"," +
            "\"keyType\":1,\"algorithm\":\"ed25519\",\"description\":\"static..bit\"}]}",
            StaticBitBackup.BuildJson(new[] { One }));
    }

    [Fact]
    public void A_Wallet_Without_A_Description_Leaves_The_Field_Out_Rather_Than_Writing_Null()
    {
        string json = StaticBitBackup.BuildJson(new[] { One with { Description = null } });
        Assert.DoesNotContain("description", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Without_A_Password_The_Header_Is_Six_Bytes_And_The_Payload_Follows_It()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, password: null);

        Assert.Equal("SBW1"u8.ToArray(), container[..4]);
        Assert.Equal(1, container[4]);
        Assert.Equal(0, container[5]);
        Assert.Equal(StaticBitBackup.BuildJson(new[] { One }), Encoding.UTF8.GetString(Inflate(container.AsSpan(6))));
    }

    [Fact]
    public void With_A_Password_The_Header_Names_The_Kdf_And_Its_Iterations()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        Assert.Equal("SBW1"u8.ToArray(), container[..4]);
        Assert.Equal(1, container[4]);
        Assert.Equal(1, container[5]);
        Assert.Equal(600_000u, BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(6, 4)));
        // 26 header + 12 nonce + 16 tag around a ciphertext the same length as the plaintext.
        Assert.True(container.Length > 26 + 12 + 16);
    }

    [Fact]
    public void A_Password_Container_Decrypts_With_The_Header_As_Associated_Data()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        byte[] header = container[..26];
        byte[] salt = container[10..26];
        byte[] nonce = container[26..38];
        byte[] tag = container[^16..];
        byte[] ciphertext = container[38..^16];

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("correct horse"), salt, 600_000, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm aes = new(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, header);

        Assert.Equal(StaticBitBackup.BuildJson(new[] { One }), Encoding.UTF8.GetString(Inflate(plaintext)));
    }

    [Fact]
    public void A_Wrong_Password_Cannot_Open_The_Container()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("wrong horse"), container[10..26], 600_000, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[container.Length - 26 - 12 - 16];
        using AesGcm aes = new(key, 16);

        Assert.Throws<AuthenticationTagMismatchException>(
            () => aes.Decrypt(container[26..38], container[38..^16], container[^16..], plaintext, container[..26]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("correct horse")]
    public void Every_Container_Starts_With_The_Prefix_The_Wallets_Scanner_Looks_For(string? password)
    {
        Assert.StartsWith("TNA11B", StaticBitBackup.ToQrText(new[] { One }, password), StringComparison.Ordinal);
    }

    [Fact]
    public void An_Empty_Set_Is_Refused_Rather_Than_Packed()
    {
        Assert.Throws<ArgumentException>(() => StaticBitBackup.Pack(Array.Empty<BackupWallet>(), null));
    }

    [Theory]
    [InlineData(null, 22)]
    [InlineData("correct horse", 21)]
    public void The_Measured_Capacity_Of_One_Code_Holds(string? password, int wallets)
    {
        // The design's table. If DEFLATE or the container ever moves, this test is what moves with
        // it — and the number in docs/2026-09-13-xrpl-vanity-qr-export-design.md moves too.
        Assert.True(StaticBitBackup.Pack(Many(wallets), password).Length <= StaticBitBackup.MaxQrContainerBytes);
        Assert.True(StaticBitBackup.Pack(Many(wallets + 1), password).Length > StaticBitBackup.MaxQrContainerBytes);
    }

    private static BackupWallet[] Many(int count)
    {
        BackupWallet[] wallets = new BackupWallet[count];
        for (int i = 0; i < count; i++)
        {
            // Distinct keys: identical entries would compress far better than real results do.
            wallets[i] = new BackupWallet(
                "sEd" + Base58Ish(26, i),
                "r" + Base58Ish(33, i + 1000),
                "static..bit");
        }

        return wallets;
    }

    private static string Base58Ish(int length, int seed)
    {
        const string Alphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";
        Random random = new(seed);
        char[] chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = Alphabet[random.Next(Alphabet.Length)];
        return new string(chars);
    }
}
