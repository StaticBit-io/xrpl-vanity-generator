using System;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// Expected work per match. A candidate matches one concrete target with probability 58^-n for
/// a suffix or substring position, but a prefix's first character follows the base58
/// leading-digit distribution (<see cref="LeadingCharacterProbability"/>) instead of a uniform
/// 58^-1, because the fixed leading 'r' consumes the address's zero version byte and leaves a
/// 192-bit number that does not divide evenly into base58 digits. The set's probability is the
/// sum over targets (overlaps ignored: an upper bound on the probability, hence a lower bound on
/// the expected candidates) and the expectation is its reciprocal. A guide, not a guarantee.
/// </summary>
public static class SearchEstimator
{
    public const int NominalAddressLength = 34;
    private const double AlphabetSize = 58.0;

    /// <summary>
    /// 2^192 / 58^32: the number of base58 digits the character right after the fixed leading
    /// 'r' can take. A classic address encodes 0x00 ‖ hash160 ‖ checksum; the zero byte becomes
    /// 'r' and the remaining 192-bit number has this many possible leading digits, so that
    /// character is (almost) uniform over alphabet indices 1..22, index 23 gets the fractional
    /// remainder, and any other character appears there only when the number is one digit
    /// shorter (probability 1 / LeadingDigitCount, then uniform over 58).
    /// </summary>
    public const double LeadingDigitCount = 23.338;

    /// <summary>Probability that <paramref name="c"/> is the character right after the leading 'r'.</summary>
    public static double LeadingCharacterProbability(char c)
    {
        int index = XrplBase58.Alphabet.IndexOf(c);
        if (index < 0)
        {
            return 0;
        }

        int fullDigits = (int)Math.Floor(LeadingDigitCount);          // 23: indices 1..22 are full
        double remainder = LeadingDigitCount - fullDigits;             // 0.338: index 23's share
        double shorter = 1.0 / (LeadingDigitCount * AlphabetSize);     // a number that lost a digit
        if (index >= 1 && index < fullDigits)
        {
            return 1.0 / LeadingDigitCount;
        }

        if (index == fullDigits)
        {
            return remainder / LeadingDigitCount;
        }

        return shorter;
    }

    public static double? ExpectedCandidatesPerMatch(PatternSet patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (!patterns.HasPatterns)
        {
            return null;
        }

        int bodyLength = NominalAddressLength - 1;
        double probability = 0;
        foreach ((MatchPosition position, string value) in patterns.EnumerateTargets())
        {
            double single = position == MatchPosition.Starts
                ? LeadingCharacterProbability(value[0]) * Math.Pow(AlphabetSize, -(value.Length - 1))
                : Math.Pow(AlphabetSize, -value.Length);
            probability += position == MatchPosition.Contains
                ? Math.Max(bodyLength - value.Length + 1, 1) * single
                : single;
        }

        foreach (PatternSet.Pair pair in patterns.Pairs)
        {
            double startsProbability = 0;
            foreach (string variant in pair.Starts)
            {
                startsProbability += LeadingCharacterProbability(variant[0]) * Math.Pow(AlphabetSize, -(pair.StartsLength - 1));
            }

            // The two halves sit at fixed, non-overlapping ends of the body, so they are
            // independent and their probabilities multiply.
            probability += startsProbability * pair.Ends.Count * Math.Pow(AlphabetSize, -pair.EndsLength);
        }

        return probability > 0 ? 1.0 / probability : null;
    }

    public static TimeSpan? ExpectedTimeToMatch(double? expectedCandidates, double ratePerSecond)
    {
        if (expectedCandidates is not double candidates || candidates <= 0 || ratePerSecond <= 0 || double.IsNaN(ratePerSecond))
        {
            return null;
        }

        double seconds = candidates / ratePerSecond;
        return seconds > TimeSpan.MaxValue.TotalSeconds ? TimeSpan.MaxValue : TimeSpan.FromSeconds(seconds);
    }
}
