using System;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class AddressDeriverTests
{
    [Fact]
    public void Seed_Roundtrips_Through_Entropy()
    {
        const string seed = "sEdTuRgwYiEcubPJfRJUArVyn7AXkVj";
        byte[] entropy = Seed.DecodeEntropy(seed);

        Assert.Equal(16, entropy.Length);
        Assert.Equal(seed, Seed.Encode(entropy));
    }

    [Fact]
    public void Derives_Known_Address_From_Known_Seed()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        IAddressDeriver deriver = new BouncyCastleDeriver();

        Span<char> buffer = stackalloc char[48];
        int len = deriver.DeriveAddress(entropy, buffer);
        string address = new string(buffer[..len]);

        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", address);
    }

    [Fact]
    public void Reused_Deriver_Produces_Correct_Address_On_Second_Call()
    {
        IAddressDeriver deriver = new BouncyCastleDeriver();
        Span<char> buffer = stackalloc char[48];

        // First call: a different (arbitrary) 16-byte entropy value, fed directly
        // without going through a seed string.
        byte[] other = new byte[16];
        for (int i = 0; i < other.Length; i++)
        {
            other[i] = (byte)(i * 17 + 1);
        }

        int len0 = deriver.DeriveAddress(other, buffer);
        string first = new string(buffer[..len0]);

        // Second call on the SAME instance: the known vector must still be exact.
        byte[] known = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        int len1 = deriver.DeriveAddress(known, buffer);
        string second = new string(buffer[..len1]);

        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", second); // reuse did not corrupt state
        Assert.NotEqual(first, second);
    }
}
