using XrplVanity.Core;

namespace XrplVanity.App.Core.ViewModels;

public sealed class PatternChip
{
    public PatternChip(string text, MatchPosition position, bool ignoreCase)
    {
        Text = text;
        Position = position;
        IgnoreCase = ignoreCase;
    }

    public string Text { get; }
    public MatchPosition Position { get; set; }
    public bool IgnoreCase { get; set; }
    /// <summary>"cannot occur in an XRPL address", "empty pattern", "longer than 12 characters: searched case-sensitively", or null.</summary>
    public string? Warning { get; internal set; }
    /// <summary>
    /// What <see cref="Text"/> is actually searched as, once the four base58 look-alikes
    /// (l, 0, O, I) are folded onto the alphabet — set by <c>SearchViewModel.RebuildPatternSet</c>
    /// only when it differs from <see cref="Text"/>, so a chip that needs no folding renders none.
    /// </summary>
    public string? Normalized { get; internal set; }
}
