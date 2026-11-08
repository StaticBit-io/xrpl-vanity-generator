using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The Search screen: its header, estimate and live figures, and the messages of its view model (estimate notes, pattern validation, search failures).</summary>
public static class SearchText
{
    public static readonly TextId Start = Id();
    public static readonly TextId Stop = Id();
    public static readonly TextId StateIdle = Id();
    public static readonly TextId StateStarting = Id();
    public static readonly TextId StateSearching = Id();
    public static readonly TextId StateStopping = Id();

    /// <summary>Plural over <c>count</c>, the number of patterns that would be cleared.</summary>
    public static readonly TextId ClearTitle = Id();
    public static readonly TextId ClearMessage = Id();
    public static readonly TextId EstimateTitle = Id();
    public static readonly TextId CandidatesPerMatch = Id();
    public static readonly TextId ExpectedTime = Id();
    public static readonly TextId EstimateAssumptions = Id();
    public static readonly TextId ReferenceRates = Id();
    public static readonly TextId LiveTitle = Id();
    public static readonly TextId LiveRate = Id();
    public static readonly TextId LiveCandidates = Id();
    public static readonly TextId LiveElapsed = Id();
    public static readonly TextId LiveFound = Id();
    public static readonly TextId TargetPending = Id();
    public static readonly TextId TargetOpening = Id();
    public static readonly TextId TargetRunning = Id();
    public static readonly TextId TargetFinished = Id();
    public static readonly TextId TargetFailed = Id();

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
