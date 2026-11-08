using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The words the confirmation dialog adds around the text its caller hands it.</summary>
public static class DialogText
{
    public static readonly TextId Cancel = Id();

    /// <summary>The default label of the confirming button.</summary>
    public static readonly TextId Delete = Id();

    /// <summary>Plural over <c>count</c>, the addresses that are not listed.</summary>
    public static readonly TextId AndMore = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Dialog", name);
}
