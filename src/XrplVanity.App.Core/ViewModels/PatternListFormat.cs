using XrplVanity.Core;

namespace XrplVanity.App.Core.ViewModels;

/// <summary>
/// The one place that knows the on-disk shape of a pattern list, so what "save list…" writes and
/// what "load list…" reads can never drift apart. Fields are tab-separated:
/// <c>ends&#9;RRRRRRRR</c>, a case-sensitive chip adding the <c>Aa</c> marker the chip itself wears,
/// a pair spelling both halves as <c>starts&#9;static&#9;ends&#9;bit</c>, and a pattern allowed at
/// the 2nd or 3rd character as <c>nearstart&#9;XRPLFILE</c>. A line with no tab at
/// all is a plain word from the older word lists (the CLI's <c>--list</c> format) and stays one.
/// </summary>
public static class PatternListFormat
{
    private const string CaseSensitiveMarker = "Aa";

    public static string Format(PatternChip chip)
    {
        string suffix = chip.IgnoreCase ? string.Empty : "\t" + CaseSensitiveMarker;
        return chip.Position == MatchPosition.Pair
            ? $"starts\t{chip.Text}\tends\t{chip.EndsText}{suffix}"
            : $"{chip.Position.ToString().ToLowerInvariant()}\t{chip.Text}{suffix}";
    }

    /// <summary>
    /// Reads one line. Returns false for a blank line and for anything outside the grammar — the
    /// caller decides which of the two it is (a blank line is skipped, a rejected one is counted).
    /// </summary>
    public static bool TryParse(string line, bool defaultIgnoreCase, out PatternChip chip)
    {
        chip = null!;
        string[] fields = line.Split('\t');
        for (int i = 0; i < fields.Length; i++) fields[i] = fields[i].Trim();

        int count = fields.Length;
        while (count > 0 && fields[count - 1].Length == 0) count--;
        if (count == 0) return false;

        // A word list line carries no separators at all; anything else has to name its position.
        if (count == 1)
        {
            if (fields.Length > 1) return false;
            chip = new PatternChip(fields[0], MatchPosition.Starts, defaultIgnoreCase);
            return true;
        }

        bool ignoreCase = true;
        if (count is 3 or 5)
        {
            if (!string.Equals(fields[count - 1], CaseSensitiveMarker, StringComparison.OrdinalIgnoreCase)) return false;
            ignoreCase = false;
            count--;
        }

        if (count == 2)
        {
            if (!TryParsePosition(fields[0], out MatchPosition position) || fields[1].Length == 0) return false;
            chip = new PatternChip(fields[1], position, ignoreCase);
            return true;
        }

        if (count == 4)
        {
            if (!string.Equals(fields[0], "starts", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(fields[2], "ends", StringComparison.OrdinalIgnoreCase)) return false;
            if (fields[1].Length == 0 || fields[3].Length == 0) return false;
            chip = new PatternChip(fields[1], MatchPosition.Pair, ignoreCase, fields[3]);
            return true;
        }

        return false;
    }

    private static bool TryParsePosition(string field, out MatchPosition position)
    {
        switch (field.ToLowerInvariant())
        {
            case "starts": position = MatchPosition.Starts; return true;
            case "ends": position = MatchPosition.Ends; return true;
            case "contains": position = MatchPosition.Contains; return true;
            case "nearstart": position = MatchPosition.NearStart; return true;
            default: position = default; return false;
        }
    }
}
