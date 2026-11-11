using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The first-run tutorial: its steps, the bubble's buttons and notes, and the leave confirmation.</summary>
public static class TutorialText
{
    public static readonly TextId OfferTitle = Id();
    public static readonly TextId OfferBody = Id();
    public static readonly TextId AddWordTitle = Id();
    public static readonly TextId AddWordBody = Id();
    public static readonly TextId ListFilesTitle = Id();

    /// <summary>Values: <c>save</c> and <c>load</c>, the labels of the two list buttons, rendered from the catalog.</summary>
    public static readonly TextId ListFilesBody = Id();
    public static readonly TextId DevicesTitle = Id();

    /// <summary>Value: <c>measure</c>, the label of the speed button.</summary>
    public static readonly TextId DevicesBody = Id();
    public static readonly TextId LongWordTitle = Id();
    public static readonly TextId LongWordBody = Id();
    public static readonly TextId ShortWordTitle = Id();
    public static readonly TextId ShortWordBody = Id();
    public static readonly TextId SearchTitle = Id();

    /// <summary>Value: <c>start</c>, the label of the Start button.</summary>
    public static readonly TextId SearchBody = Id();
    public static readonly TextId ResultTitle = Id();
    public static readonly TextId ResultBody = Id();
    public static readonly TextId QrButtonTitle = Id();
    public static readonly TextId QrButtonBody = Id();
    public static readonly TextId QrDialogTitle = Id();
    public static readonly TextId QrDialogBody = Id();
    public static readonly TextId GpuDutyTitle = Id();
    public static readonly TextId GpuDutyBody = Id();
    public static readonly TextId StopAfterTitle = Id();
    public static readonly TextId StopAfterBody = Id();
    public static readonly TextId NotificationsTitle = Id();
    public static readonly TextId NotificationsBody = Id();
    public static readonly TextId DataFolderTitle = Id();
    public static readonly TextId DataFolderBody = Id();
    public static readonly TextId DoneTitle = Id();
    public static readonly TextId DoneBody = Id();

    public static readonly TextId ShortestWordHint = Id();

    /// <summary>Values: <c>number</c>, the step shown, and <c>count</c>, the steps in all.</summary>
    public static readonly TextId StepCounter = Id();
    public static readonly TextId SearchRunning = Id();
    public static readonly TextId Leaving = Id();
    public static readonly TextId NotNow = Id();
    public static readonly TextId StartTutorial = Id();
    public static readonly TextId Skip = Id();
    public static readonly TextId Next = Id();
    public static readonly TextId Finish = Id();
    public static readonly TextId LeaveTitle = Id();
    public static readonly TextId LeaveMessage = Id();
    public static readonly TextId LeaveConfirm = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Tutorial", name);
}
