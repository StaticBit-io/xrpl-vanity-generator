using System;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchEstimatorTests
{
    private const double Leading = 23.338;

    private static PatternSet Set(MatchPosition position, string pattern, bool ignoreCase = false) =>
        new PatternSet.Builder().Add(position, pattern, ignoreCase).Build();

    [Theory]
    [InlineData("a", 1)]
    [InlineData("ab", 58)]
    [InlineData("abc", 3364)]
    [InlineData("abcd", 195_112)]
    public void Case_Sensitive_Prefix_Uses_The_Leading_Digit_Count_Then_58_Per_Character(string pattern, double restFactor)
    {
        // First character after 'r': one of ~23.338 leading digits; every later character: one of 58.
        Assert.Equal(Leading * restFactor, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, pattern))!.Value, precision: 3);
    }

    [Fact]
    public void Suffix_Expects_58_To_The_Length()
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

    [Theory]
    [InlineData('p', 1 / 23.338)]                 // alphabet index 1
    [InlineData('P', 1 / 23.338)]                 // alphabet index 22
    [InlineData('Q', 0.338 / 23.338)]             // alphabet index 23: the partial leading digit
    [InlineData('c', 1 / (23.338 * 58))]          // index 35: only when the number loses a digit
    [InlineData('r', 1 / (23.338 * 58))]          // index 0: same
    [InlineData('z', 1 / (23.338 * 58))]          // index 57: same
    public void Leading_Character_Probability_Follows_The_Base58_Digit_Distribution(char c, double expected)
    {
        Assert.Equal(expected, SearchEstimator.LeadingCharacterProbability(c), precision: 9);
    }

    [Fact]
    public void Leading_Character_Probabilities_Sum_To_About_One()
    {
        double sum = 0;
        foreach (char c in XrplBase58.Alphabet) sum += SearchEstimator.LeadingCharacterProbability(c);
        Assert.InRange(sum, 0.98, 1.0);
    }

    [Fact]
    public void Case_Insensitive_Letters_With_Two_Cases_Double_The_Targets()
    {
        // "ab" ignore-case expands to ab, aB, Ab, AB: a and A both lead ('a' index 5, 'A' index 53).
        double a = SearchEstimator.LeadingCharacterProbability('a') / 58 + SearchEstimator.LeadingCharacterProbability('A') / 58;
        Assert.Equal(1.0 / (2 * a), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "ab", ignoreCase: true))!.Value, precision: 3);
        // "o" has no upper-case form and "L" no lower-case form: no expansion.
        Assert.Equal(1.0 / SearchEstimator.LeadingCharacterProbability('o'), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "o", ignoreCase: true))!.Value, precision: 3);
        Assert.Equal(1.0 / SearchEstimator.LeadingCharacterProbability('L'), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "L", ignoreCase: true))!.Value, precision: 3);
    }

    [Fact]
    public void Several_Patterns_Add_Their_Probabilities()
    {
        PatternSet two = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: false)
            .Add(MatchPosition.Ends, "b", ignoreCase: false)
            .Build();
        double expected = 1.0 / (SearchEstimator.LeadingCharacterProbability('a') + 1.0 / 58);
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(two)!.Value, precision: 3);
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
