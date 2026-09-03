using System;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class XrplBase58Tests
{
    [Fact]
    public void Encode_Then_Decode_Roundtrips()
    {
        byte[] input = { 0x00, 0x01, 0x02, 0xED, 0xFF, 0x10, 0x20, 0x30, 0x40, 0x50 };
        Span<char> buffer = stackalloc char[64];
        int len = XrplBase58.Encode(input, buffer);
        string encoded = new string(buffer[..len]);

        byte[] decoded = XrplBase58.Decode(encoded);

        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Uses_Xrpl_Alphabet_First_Char_For_Leading_Zero()
    {
        byte[] input = { 0x00, 0x00, 0x05 };
        Span<char> buffer = stackalloc char[16];
        int len = XrplBase58.Encode(input, buffer);
        string encoded = new string(buffer[..len]);

        Assert.StartsWith("rr", encoded); // 'r' is alphabet[0]; two leading zero bytes -> "rr"
    }
}
