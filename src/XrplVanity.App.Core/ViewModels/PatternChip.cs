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
}
