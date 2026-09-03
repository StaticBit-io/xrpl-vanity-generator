using System.Collections.Generic;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliRuntimeTests
{
    [Fact]
    public void SearchDeterministic_Finds_Match_In_A_Fixed_Entropy_Stream()
    {
        CliOptions options = CliOptions.Parse(new[] { "disk" });
        byte[] winning = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        List<byte[]> stream = new()
        {
            new byte[16], // no match
            winning,      // matches "disk"
        };

        Match? found = SearchRunner.SearchDeterministic(options, stream);

        Assert.NotNull(found);
        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", found!.Value.Address);
    }
}
