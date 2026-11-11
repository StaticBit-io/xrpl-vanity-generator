using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>Windows toasts raised by the search.</summary>
public static class NotificationText
{
    public static readonly TextId MatchFoundTitle = Id();

    /// <summary>Values: <c>address</c>, <c>pattern</c>, <c>position</c>.</summary>
    public static readonly TextId MatchFoundBody = Id();
    public static readonly TextId MatchesFoundTitle = Id();

    /// <summary>Plural over <c>count</c>; value: <c>address</c>, the latest match.</summary>
    public static readonly TextId MatchesFoundBody = Id();
    public static readonly TextId SearchFinishedTitle = Id();

    /// <summary>Plural over <c>count</c>; value: <c>outcome</c>, one of the <c>Outcome*</c> ids.</summary>
    public static readonly TextId SearchFinishedBody = Id();

    public static readonly TextId OutcomeCountReached = Id();
    public static readonly TextId OutcomeCancelled = Id();
    public static readonly TextId OutcomeAllTargetsFailed = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Notification", name);
}
