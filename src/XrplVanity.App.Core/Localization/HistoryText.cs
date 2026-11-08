using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>Why the result history cannot be written: shown as a banner on History and on Search.</summary>
public static class HistoryText
{
    public static readonly TextId ReadOnly = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text.</summary>
    public static readonly TextId ResultsCouldNotBeRead = Id();

    /// <summary>Value: <c>detail</c>.</summary>
    public static readonly TextId ResultsCouldNotBeWritten = Id();

    /// <summary>Value: <c>detail</c>; a batch of matches that did not reach disk during a search.</summary>
    public static readonly TextId ResultsNotSaved = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("History", name);
}
