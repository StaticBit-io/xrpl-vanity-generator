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
}
