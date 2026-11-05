namespace XrplVanity.App.Core.Tutorial;

/// <summary>
/// The tutorial's steps in the order a user meets them. The numeric value is the step's index in
/// <see cref="TutorialScript.Steps"/>, so the two must stay in the same order.
/// </summary>
public enum TutorialStep
{
    Offer,
    AddWord,
    ListFiles,
    Devices,
    LongWord,
    ShortWord,
    Search,
    Result,
    QrButton,
    QrDialog,
    GpuDuty,
    StopAfter,
    NotificationsSetting,
    DataFolder,
    Done,
}

/// <summary>
/// What one step shows. <paramref name="Lit"/> names the page regions that stay visible; every other
/// region is dimmed and inert. When <paramref name="Interactive"/> is false the lit regions are
/// inert too: the step only explains them.
/// </summary>
public sealed record TutorialStepInfo(
    TutorialStep Step,
    string Route,
    IReadOnlyList<string> Lit,
    bool Interactive,
    string Title,
    string Body);
