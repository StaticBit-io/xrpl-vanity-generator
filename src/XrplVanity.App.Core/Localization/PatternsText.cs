using System.Runtime.CompilerServices;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Localization;

/// <summary>The Patterns card: the chips, the entry row, list files and the words for where a pattern matches.</summary>
public static class PatternsText
{
    public static readonly TextId Title = Id();
    public static readonly TextId Clear = Id();
    public static readonly TextId Remove = Id();

    /// <summary>Value: <c>count</c>, how many chips are hidden.</summary>
    public static readonly TextId MoreChips = Id();
    public static readonly TextId ShowLess = Id();

    /// <summary>Where a pattern matches, as a lower-case word; see <see cref="Position"/>.</summary>
    public static readonly TextId PositionStarts = Id();
    public static readonly TextId PositionEnds = Id();
    public static readonly TextId PositionContains = Id();
    public static readonly TextId PositionPair = Id();
    public static readonly TextId PositionNearStart = Id();

    /// <summary>Value: <c>value</c>, a stored position this version does not know, shown as it is.</summary>
    public static readonly TextId Raw = Id();

    public static readonly TextId NearRange = Id();
    public static readonly TextId NearRangeTitlePair = Id();
    public static readonly TextId NearRangeTitle = Id();
    public static readonly TextId NormalizedTitle = Id();
    public static readonly TextId CaseSensitiveTitle = Id();
    public static readonly TextId WarningUnreachable = Id();
    public static readonly TextId WarningEmpty = Id();

    /// <summary>Plural over <c>count</c>, the longest pattern that is folded to ignore case.</summary>
    public static readonly TextId WarningCaseFoldCap = Id();

    /// <summary>Value: <c>speedup</c> (a factor, shown without decimals).</summary>
    public static readonly TextId NearStartSpeedup = Id();
    public static readonly TextId NearStartSpeedupTitle = Id();

    public static readonly TextId StartsWith = Id();
    public static readonly TextId EndsWith = Id();
    public static readonly TextId StartsAndEndsWith = Id();
    public static readonly TextId StartsWithPlaceholder = Id();
    public static readonly TextId EndsWithPlaceholder = Id();
    public static readonly TextId EntryPlaceholder = Id();
    public static readonly TextId IgnoreCase = Id();
    public static readonly TextId NearStartOption = Id();
    public static readonly TextId NearStartOptionTitle = Id();
    public static readonly TextId LoadList = Id();
    public static readonly TextId SaveList = Id();

    /// <summary>The name of the pattern list file type in the save dialog's type filter.</summary>
    public static readonly TextId ListFileType = Id();
    public static readonly TextId AlreadyInList = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text.</summary>
    public static readonly TextId ListReadFailed = Id();

    /// <summary>Value: <c>detail</c>.</summary>
    public static readonly TextId ListSaveFailed = Id();

    /// <summary>Plural over <c>count</c>.</summary>
    public static readonly TextId DuplicatesSkipped = Id();

    /// <summary>Plural over <c>count</c>.</summary>
    public static readonly TextId LinesSkipped = Id();

    /// <summary>Values: <c>duplicates</c> and <c>lines</c>, the two sentences above.</summary>
    public static readonly TextId DuplicatesAndLinesSkipped = Id();

    /// <summary>
    /// The word for a stored <see cref="MatchPosition"/> name (<c>SessionResult.Position</c>, <c>StoredResult.Position</c>).
    /// The stored string is a file format and never changes; a name this version cannot parse is shown as stored.
    /// </summary>
    public static UiText Position(string storedName)
    {
        if (Enum.TryParse(storedName, out MatchPosition position) && Enum.IsDefined(position))
        {
            return UiText.Of(position switch
            {
                MatchPosition.Starts => PositionStarts,
                MatchPosition.Ends => PositionEnds,
                MatchPosition.Contains => PositionContains,
                MatchPosition.Pair => PositionPair,
                _ => PositionNearStart,
            });
        }

        return UiText.Of(Raw, ("value", storedName));
    }

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Patterns", name);
}
