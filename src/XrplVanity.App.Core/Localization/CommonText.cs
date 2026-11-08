using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>Words shared by several screens: durations and the text for a value that is not known yet.</summary>
public static class CommonText
{
    /// <summary>Plural over <c>count</c>, a number of days with one decimal.</summary>
    public static readonly TextId Days = Id();
    public static readonly TextId Hours = Id();
    public static readonly TextId Minutes = Id();
    public static readonly TextId Seconds = Id();
    public static readonly TextId MoreThanAYear = Id();
    public static readonly TextId LessThanASecond = Id();

    /// <summary>The dash shown in place of a value that is not known yet.</summary>
    public static readonly TextId NoValue = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Common", name);
}
