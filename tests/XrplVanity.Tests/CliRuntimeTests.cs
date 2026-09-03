using System.Collections.Generic;
using System.IO;
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

    [Fact]
    public void BuildPatternSet_Skips_An_Empty_Pattern_Instead_Of_Matching_Everything()
    {
        CliOptions options = CliOptions.Parse(new[] { "--starts", "" });

        PatternSet patterns = SearchRunner.BuildPatternSet(options);

        // A real, unrelated address must not match: an empty pattern normalizes to a
        // zero-length bucket that (if not skipped) matches every address.
        Assert.False(patterns.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void BuildPatternSet_Skips_Blank_Lines_From_A_List_File_Regardless_Of_MinLength()
    {
        string listFile = Path.GetTempFileName();
        try
        {
            // A blank line plus a min-length of 0 used to slip an empty pattern past the
            // ListMinLength gate and into the "starts" bucket.
            File.WriteAllText(listFile, "\n   \n");
            CliOptions options = CliOptions.Parse(new[] { "--list", listFile, "0" });

            PatternSet patterns = SearchRunner.BuildPatternSet(options);

            Assert.False(patterns.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
        }
        finally
        {
            File.Delete(listFile);
        }
    }
}
