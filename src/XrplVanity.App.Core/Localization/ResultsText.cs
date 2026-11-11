using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The results list shared by Search and History.</summary>
public static class ResultsText
{
    public static readonly TextId Title = Id();
    public static readonly TextId ExportTsv = Id();

    /// <summary>The name of the TSV file type in the save dialog's type filter.</summary>
    public static readonly TextId TsvFileType = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text.</summary>
    public static readonly TextId ExportFailed = Id();
    public static readonly TextId SelectAllShown = Id();
    public static readonly TextId SelectAllShownLabel = Id();

    /// <summary>Values: <c>selected</c>, <c>total</c>.</summary>
    public static readonly TextId SelectAllShownPartialLabel = Id();

    /// <summary>Value: <c>count</c>.</summary>
    public static readonly TextId SelectedCount = Id();
    public static readonly TextId ExportSelected = Id();
    public static readonly TextId ShowQr = Id();
    public static readonly TextId DeleteSelected = Id();
    public static readonly TextId ClearSelection = Id();
    public static readonly TextId Empty = Id();

    /// <summary>Value: <c>address</c>.</summary>
    public static readonly TextId SelectRow = Id();
    public static readonly TextId CopyAddress = Id();
    public static readonly TextId RevealSeed = Id();
    public static readonly TextId HideSeed = Id();

    /// <summary>Value: <c>seconds</c>, when the clipboard is cleared.</summary>
    public static readonly TextId CopySeed = Id();
    public static readonly TextId ShowAsQr = Id();
    public static readonly TextId Delete = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Results", name);
}
