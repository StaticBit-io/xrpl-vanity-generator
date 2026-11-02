namespace XrplVanity.Core;

public enum MatchPosition
{
    Starts,
    Ends,
    Contains,
    Pair,

    /// <summary>
    /// The pattern begins at the 2nd or the 3rd character of the address (the first or second
    /// character of the body after the fixed leading 'r'). Opt-in, because the address then reads
    /// <c>r?WORD…</c>. It exists for patterns whose first character can almost never follow the
    /// 'r': the character after it carries the base58 leading-digit bias, the one after that does
    /// not, so such a pattern is found about 24 times sooner one character later.
    /// </summary>
    NearStart,
}
