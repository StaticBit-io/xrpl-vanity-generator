using System;
using System.Security.Cryptography;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CrossCheckTests
{
    [Fact]
    public void Derived_Address_Matches_Sdk_For_Random_Seeds()
    {
        IAddressDeriver deriver = new BouncyCastleDeriver();

        for (int i = 0; i < 500; i++)
        {
            byte[] entropy = RandomNumberGenerator.GetBytes(16);

            Span<char> buffer = stackalloc char[48];
            int len = deriver.DeriveAddress(entropy, buffer);
            string ours = new string(buffer[..len]);

            string sdkSeed = Xrpl.Keypairs.XrplKeypairs.GenerateSeed(entropy, "ed25519");
            Xrpl.Keypairs.IXrplKeyPair kp = Xrpl.Keypairs.XrplKeypairs.DeriveKeypair(sdkSeed);
            string sdk = Xrpl.Keypairs.XrplKeypairs.DeriveAddress(kp.Id());

            Assert.Equal(sdk, ours);
            Assert.Equal(sdkSeed, Seed.Encode(entropy));
        }
    }
}
