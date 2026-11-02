using XrplVanity.Core;

namespace XrplVanity.App.Core.ViewModels;

public sealed class PatternChip
{
    public PatternChip(string text, MatchPosition position, bool ignoreCase, string? endsText = null)
    {
        Text = text;
        Position = position;
        IgnoreCase = ignoreCase;
        EndsText = endsText;
    }

    public string Text { get; }

    /// <summary>
    /// The second half of a <see cref="MatchPosition.Pair"/> chip — the suffix the address body
    /// must also end with — and null for every other chip. Set in the constructor like
    /// <see cref="Text"/>, so the two halves of one rule can never drift apart; a chip flipped to
    /// <see cref="MatchPosition.Pair"/> by <c>SearchViewModel.UpdatePattern</c> therefore has none,
    /// contributes no rule, and carries the "empty pattern" <see cref="Warning"/> instead.
    /// </summary>
    public string? EndsText { get; }

    public MatchPosition Position { get; set; }
    public bool IgnoreCase { get; set; }
    /// <summary>"cannot occur in an XRPL address", "empty pattern", "longer than 12 characters: searched case-sensitively", or null.</summary>
    public string? Warning { get; internal set; }
    /// <summary>
    /// What <see cref="Text"/> is actually searched as, once the four base58 look-alikes
    /// (l, 0, O, I) are folded onto the alphabet — set by <c>SearchViewModel.RebuildPatternSet</c>
    /// only when it differs from <see cref="Text"/>, so a chip that needs no folding renders none.
    /// A paired chip records both halves as <c>starts..ends</c>, the form <c>PatternSet</c> reports.
    /// </summary>
    public string? Normalized { get; internal set; }

    /// <summary>
    /// For a <see cref="MatchPosition.Starts"/> chip whose first character can rarely follow the
    /// leading 'r': how many times sooner it would be found as <see cref="MatchPosition.NearStart"/>.
    /// Null otherwise. Set by <c>SearchViewModel.RebuildPatternSet</c>.
    /// </summary>
    public double? NearStartSpeedup { get; internal set; }

    /// <summary>
    /// What makes two chips the same rule, so a set never holds one twice. Built from the
    /// normalized halves rather than the typed ones — <c>starts 00000000</c> and
    /// <c>starts oooooooo</c> search for exactly the same thing — and folds case only for a chip
    /// that ignores it, since a case-sensitive rule is narrower than its case-insensitive twin and
    /// the two belong in the set together.
    /// </summary>
    public string Identity
    {
        get
        {
            string text = Fold(Text, IgnoreCase);
            string ends = EndsText is null ? string.Empty : Fold(EndsText.Trim(), IgnoreCase);
            return $"{Position}|{IgnoreCase}|{text}|{ends}";
        }
    }

    private static string Fold(string value, bool ignoreCase)
    {
        string normalized = PatternNormalizer.Normalize(value).Value;
        return ignoreCase ? normalized.ToLowerInvariant() : normalized;
    }
}
