using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The History page, and why the result history cannot be written (shown as a banner on History and on Search).</summary>
public static class HistoryText
{
    public static readonly TextId ReadOnly = Id();

    public static readonly TextId Title = Id();

    /// <summary>Value: <c>count</c>, the number of stored results.</summary>
    public static readonly TextId Stored = Id();
    public static readonly TextId ClearHistory = Id();
    public static readonly TextId FilterPlaceholder = Id();
    public static readonly TextId FilterByPattern = Id();

    /// <summary>Value: <c>count</c>.</summary>
    public static readonly TextId AllPatterns = Id();

    /// <summary>Values: <c>pattern</c> and <c>position</c> (a <see cref="UiText"/>), the same cell the pattern column shows.</summary>
    public static readonly TextId PatternOption = Id();

    /// <summary>Values: <c>label</c> (a <see cref="UiText"/>) and <c>count</c>, one choice of the pattern filter.</summary>
    public static readonly TextId OptionCount = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text of a write that failed.</summary>
    public static readonly TextId ActionFailed = Id();

    /// <summary>A sentence that names <c>results.dat</c>; the page shows that name in the monospace face.</summary>
    public static readonly TextId StorageNote = Id();
    public static readonly TextId DeleteOneTitle = Id();
    public static readonly TextId DeleteOneMessage = Id();

    /// <summary>Plural over <c>count</c>.</summary>
    public static readonly TextId DeleteManyTitle = Id();
    public static readonly TextId DeleteManyMessage = Id();

    /// <summary>Value: <c>count</c>.</summary>
    public static readonly TextId DeleteManyConfirm = Id();
    public static readonly TextId ClearTitle = Id();

    /// <summary>Plural over <c>count</c>, the number of stored results.</summary>
    public static readonly TextId ClearMessage = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text.</summary>
    public static readonly TextId ResultsCouldNotBeRead = Id();

    /// <summary>Value: <c>detail</c>.</summary>
    public static readonly TextId ResultsCouldNotBeWritten = Id();

    /// <summary>Value: <c>detail</c>; a batch of matches that did not reach disk during a search.</summary>
    public static readonly TextId ResultsNotSaved = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("History", name);
}
