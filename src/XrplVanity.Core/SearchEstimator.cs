using System;
using System.Collections.Generic;

namespace XrplVanity.Core;

/// <summary>
/// Expected work per match. A candidate matches one concrete target with probability 58^-n for
/// a suffix or substring position, but a prefix's first character follows the base58
/// leading-digit distribution (<see cref="LeadingCharacterProbability"/>) instead of a uniform
/// 58^-1, because the fixed leading 'r' consumes the address's zero version byte and leaves a
/// 192-bit number that does not divide evenly into base58 digits. The set's probability is the
/// sum over targets and paired rules (overlaps ignored: an upper bound on the probability, hence
/// a lower bound on the expected candidates) and the expectation is its reciprocal. A guide, not
/// a guarantee.
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

    /// <summary>Probability that the body starts with <paramref name="value"/>.</summary>
    public static double StartsProbability(string value) =>
        LeadingCharacterProbability(value[0]) * Math.Pow(AlphabetSize, -(value.Length - 1));

    /// <summary>
    /// How many times sooner a pattern is expected to match as <see cref="MatchPosition.NearStart"/>
    /// than as <see cref="MatchPosition.Starts"/>, given the characters its case variants can begin
    /// with. Only the first character matters: every variant has the same length and the rest of
    /// it costs the same at either offset. About 24 when none of them can follow the leading 'r'
    /// of a full-length address, about 1.4 when all of them can.
    /// </summary>
    public static double NearStartSpeedup(IReadOnlyCollection<char> firstCharacters)
    {
        ArgumentNullException.ThrowIfNull(firstCharacters);
        double starts = 0;
        double near = 0;
        foreach (char c in firstCharacters)
        {
            double p = LeadingCharacterProbability(c);
            starts += p;
            near += p + 1.0 / AlphabetSize;
        }

        return starts > 0 ? near / starts : 1;
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
            double single = position switch
            {
                MatchPosition.Starts => StartsProbability(value),
                // One character later the leading-digit bias is gone, so the second offset costs
                // a plain 58^-n; a value matching at both offsets at once is ignored like overlaps.
                MatchPosition.NearStart => StartsProbability(value) + Math.Pow(AlphabetSize, -value.Length),
                _ => Math.Pow(AlphabetSize, -value.Length),
            };
            probability += position == MatchPosition.Contains
                ? Math.Max(bodyLength - value.Length + 1, 1) * single
                : single;
        }

        foreach (PatternSet.Pair pair in patterns.Pairs)
        {
            double startsProbability = 0;
            foreach (string variant in pair.Starts)
            {
                startsProbability += StartsProbability(variant);
                if (pair.NearStart)
                {
                    startsProbability += Math.Pow(AlphabetSize, -variant.Length);
                }
            }

            // The two halves sit at fixed, non-overlapping ends of the body, so they are
            // independent and their probabilities multiply. The end half needs no per-variant
            // sum: every one of its variants costs the same 58^-EndsLength, while the start
            // half's variants differ in their leading character.
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
