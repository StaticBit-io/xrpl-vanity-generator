using System;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// Expected work per match. A candidate matches one concrete target of length n with
/// probability 58^-n for a prefix or suffix (the body excludes the fixed leading 'r'), and
/// (body length - n + 1) × 58^-n for a substring at any position of a nominal 34-character
/// address. The set's probability is the sum over targets (overlaps ignored: an upper bound
/// on the probability, hence a lower bound on the expected candidates) and the expectation is
/// its reciprocal. A guide, not a guarantee.
/// </summary>
public static class SearchEstimator
{
    public const int NominalAddressLength = 34;
    private const double AlphabetSize = 58.0;

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
            double single = Math.Pow(AlphabetSize, -value.Length);
            probability += position == MatchPosition.Contains
                ? Math.Max(bodyLength - value.Length + 1, 1) * single
                : single;
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
