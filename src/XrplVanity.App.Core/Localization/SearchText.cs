using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>Messages of the Search screen's view model: estimate notes, pattern validation and search failures.</summary>
public static class SearchText
{
    public static readonly TextId AddPatternForEstimate = Id();
    public static readonly TextId RateUnknown = Id();
    public static readonly TextId EstimateFromLiveRate = Id();
    public static readonly TextId EstimateFromLastMeasuredRate = Id();

    /// <summary>Values: <c>pattern</c>, <c>speedup</c> (a factor, shown without decimals).</summary>
    public static readonly TextId NearStartHint = Id();
    public static readonly TextId AddAtLeastOnePattern = Id();

    /// <summary>Value: <c>pattern</c>.</summary>
    public static readonly TextId PatternUnreachable = Id();

    /// <summary>Values: <c>typed</c>, <c>normalized</c>.</summary>
    public static readonly TextId PatternSearchedAs = Id();

    /// <summary>Value: <c>typed</c>.</summary>
    public static readonly TextId PatternPressEnter = Id();
    public static readonly TextId SelectTarget = Id();

    /// <summary>Value: <c>detail</c>, the lower layer's own text.</summary>
    public static readonly TextId SearchFailed = Id();
    public static readonly TextId SearchCancelled = Id();
    public static readonly TextId NoTargetCouldStart = Id();

    /// <summary>Value: <c>detail</c>, the failure of the first target that could not start.</summary>
    public static readonly TextId AllTargetsFailed = Id();

    /// <summary>Value: <c>detail</c>, a warning or error line the search engine logged.</summary>
    public static readonly TextId SearchLogLine = Id();

    /// <summary>Value: <c>detail</c>.</summary>
    public static readonly TextId CalibrationFailed = Id();
    public static readonly TextId CalibrationCancelled = Id();
    public static readonly TextId NoTargetCouldBeMeasured = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Search", name);
}
