using System;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchEstimatorTests
{
    private static PatternSet Set(MatchPosition position, string pattern, bool ignoreCase = false) =>
        new PatternSet.Builder().Add(position, pattern, ignoreCase).Build();

    [Theory]
    [InlineData("a", 58)]
    [InlineData("ab", 3364)]
    [InlineData("abc", 195_112)]
    [InlineData("abcd", 11_316_496)]
    public void Case_Sensitive_Prefix_Expects_58_To_The_Length(string pattern, double expected)
    {
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, pattern))!.Value, precision: 6);
    }

    [Fact]
    public void Suffix_Has_The_Same_Expectation_As_A_Prefix()
    {
        Assert.Equal(3364, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Ends, "ab"))!.Value, precision: 6);
    }

    [Fact]
    public void Contains_Counts_Every_Position_In_A_Nominal_Address_Body()
    {
        // body length 33 (34 minus the leading r); "ab" fits in 32 positions.
        double expected = 3364.0 / 32.0;
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Contains, "ab"))!.Value, precision: 6);
    }

    [Fact]
    public void Case_Insensitive_Letters_With_Two_Cases_Double_The_Targets()
    {
        // "ab": a/A and b/B both exist in the alphabet -> 4 variants -> 58^2 / 4.
        Assert.Equal(3364.0 / 4.0, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "ab", ignoreCase: true))!.Value, precision: 6);
        // "o" has no upper-case form and "L" no lower-case form: no expansion.
        Assert.Equal(58, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "o", ignoreCase: true))!.Value, precision: 6);
        Assert.Equal(58, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "L", ignoreCase: true))!.Value, precision: 6);
    }

    [Fact]
    public void Several_Patterns_Add_Their_Probabilities()
    {
        PatternSet two = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: false)
            .Add(MatchPosition.Ends, "b", ignoreCase: false)
            .Build();
        Assert.Equal(29, SearchEstimator.ExpectedCandidatesPerMatch(two)!.Value, precision: 6);
    }

    [Fact]
    public void An_Empty_Set_Has_No_Estimate()
    {
        PatternSet none = new PatternSet.Builder().Add(MatchPosition.Starts, "@", ignoreCase: false).Build();
        Assert.Null(SearchEstimator.ExpectedCandidatesPerMatch(none));
    }

    [Fact]
    public void Eta_Divides_Candidates_By_Rate_And_Propagates_Null()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SearchEstimator.ExpectedTimeToMatch(1000, 100));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(null, 100));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(1000, 0));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(1000, -5));
    }
}
