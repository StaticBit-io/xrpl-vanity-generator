using System.Text;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class Base45Tests
{
    [Theory]
    [InlineData("AB", "BB8")]
    [InlineData("Hello!!", "%69 VD92EX0")]
    [InlineData("base-45", "UJCLQE7W581")]
    [InlineData("ietf!", "QED8WEX0")]
    public void Rfc9285_Vectors_Encode_As_Published(string text, string expected)
    {
        Assert.Equal(expected, Base45.Encode(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void An_Odd_Trailing_Byte_Becomes_Two_Characters()
    {
        // A pair becomes three characters, a lone byte two: 3 bytes therefore give 5.
        Assert.Equal(5, Base45.Encode(new byte[] { 0x53, 0x42, 0x57 }).Length);
    }

    [Fact]
    public void Empty_Input_Encodes_As_An_Empty_String()
    {
        Assert.Equal(string.Empty, Base45.Encode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void The_Magic_Bytes_Encode_As_The_Prefix_The_Wallet_Looks_For()
    {
        Assert.Equal("TNA11B", Base45.Encode("SBW1"u8));
    }
}
