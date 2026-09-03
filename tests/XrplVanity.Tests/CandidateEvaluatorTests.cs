using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CandidateEvaluatorTests
{
    [Fact]
    public void Reports_Match_With_Seed_For_Known_Vector()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        PatternSet patterns = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: true)
            .Build();
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);

        bool matched = evaluator.TryEvaluate(entropy, out Match match);

        Assert.True(matched);
        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", match.Address);
        Assert.Equal("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj", match.Seed);
        Assert.Equal(MatchPosition.Starts, match.Position);
    }

    [Fact]
    public void Reports_No_Match_When_Patterns_Absent()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        PatternSet patterns = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "zzzz", ignoreCase: true)
            .Build();
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);

        Assert.False(evaluator.TryEvaluate(entropy, out _));
    }
}
