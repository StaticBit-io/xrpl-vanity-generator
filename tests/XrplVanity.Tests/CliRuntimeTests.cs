using System;
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

    [Fact]
    public void BuildPatternSet_Reports_No_Patterns_When_All_Are_Unreachable()
    {
        CliOptions options = CliOptions.Parse(new[] { "bit@coin" }); // '@' is not in the base58 alphabet
        PatternSet patterns = SearchRunner.BuildPatternSet(options);

        Assert.False(patterns.HasPatterns);
        Assert.Single(patterns.Unreachable);
    }

    [Fact]
    public void BuildPatternSet_Turns_A_Pair_Option_Into_A_Paired_Rule()
    {
        CliOptions options = CliOptions.Parse(new[] { "--case-sensitive", "--starts-ends", "ab:yz" });
        PatternSet set = SearchRunner.BuildPatternSet(options);
        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);
    }

    [Fact]
    public void SearchDeterministic_Finds_Match_For_A_Starts_Ends_Pair()
    {
        // A one-character prefix and suffix keep the brute-force search below a few thousand
        // deterministic entropies while still exercising the real derive-then-match path.
        CliOptions options = CliOptions.Parse(new[] { "--case-sensitive", "--starts-ends", "a:b" });
        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);

        byte[]? winningEntropy = null;
        for (int i = 0; i < 5000; i++)
        {
            byte[] entropy = new byte[16];
            BitConverter.TryWriteBytes(entropy, i);
            if (evaluator.TryEvaluate(entropy, out Match candidate))
            {
                winningEntropy = entropy;
                break;
            }
        }

        Assert.NotNull(winningEntropy);

        List<byte[]> stream = new()
        {
            new byte[16], // all-zero entropy: not expected to match "a"/"b"
            winningEntropy!,
        };

        Match? found = SearchRunner.SearchDeterministic(options, stream);

        Assert.NotNull(found);
        Assert.Equal(MatchPosition.Pair, found!.Value.Position);
        Assert.StartsWith("a", found.Value.Address[1..], StringComparison.Ordinal);
        Assert.EndsWith("b", found.Value.Address, StringComparison.Ordinal);
    }
}
