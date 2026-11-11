using XrplVanity.App.Core.Localization;

namespace XrplVanity.App.Core.Tutorial;

/// <summary>The page regions a step can light, named once so the script and the pages cannot drift apart.</summary>
public static class TutorialRegions
{
    public const string Title = "title";
    public const string Nav = "nav";
    public const string Start = "start";
    public const string Patterns = "patterns";
    public const string Estimate = "estimate";
    public const string Devices = "devices";
    public const string Duty = "duty";
    public const string Stats = "stats";
    public const string Results = "results";
    public const string SettingStop = "setting-stop";
    public const string SettingNotifications = "setting-notifications";
    public const string SettingData = "setting-data";
    public const string SettingsOther = "settings-other";
}

/// <summary>The fixed list of steps with their copy. Indexed by <see cref="TutorialStep"/>.</summary>
public static class TutorialScript
{
    private static readonly string[] None = Array.Empty<string>();

    public static IReadOnlyList<TutorialStepInfo> Steps { get; } = new TutorialStepInfo[]
    {
        new(TutorialStep.Offer, "/", None, false,
            TutorialText.OfferTitle, TutorialText.OfferBody),
        new(TutorialStep.AddWord, "/", new[] { TutorialRegions.Patterns }, true,
            TutorialText.AddWordTitle, TutorialText.AddWordBody),
        // A body names a button by the catalog's own label for it, so the step cannot drift from the page in any language.
        new(TutorialStep.ListFiles, "/", new[] { TutorialRegions.Patterns }, true,
            TutorialText.ListFilesTitle,
            UiText.Of(TutorialText.ListFilesBody, ("save", PatternsText.SaveList), ("load", PatternsText.LoadList))),
        new(TutorialStep.Devices, "/", new[] { TutorialRegions.Devices }, true,
            TutorialText.DevicesTitle,
            UiText.Of(TutorialText.DevicesBody, ("measure", DevicesText.MeasureSpeed))),
        new(TutorialStep.LongWord, "/", new[] { TutorialRegions.Patterns, TutorialRegions.Estimate }, true,
            TutorialText.LongWordTitle, TutorialText.LongWordBody),
        new(TutorialStep.ShortWord, "/", new[] { TutorialRegions.Patterns, TutorialRegions.Estimate }, true,
            TutorialText.ShortWordTitle, TutorialText.ShortWordBody),
        new(TutorialStep.Search, "/", new[] { TutorialRegions.Start, TutorialRegions.Patterns, TutorialRegions.Estimate, TutorialRegions.Stats, TutorialRegions.Results }, true,
            TutorialText.SearchTitle,
            UiText.Of(TutorialText.SearchBody, ("start", SearchText.Start))),
        new(TutorialStep.Result, "/", new[] { TutorialRegions.Results }, false,
            TutorialText.ResultTitle, TutorialText.ResultBody),
        new(TutorialStep.QrButton, "/", new[] { TutorialRegions.Results }, true,
            TutorialText.QrButtonTitle, TutorialText.QrButtonBody),
        new(TutorialStep.QrDialog, "/", None, false,
            TutorialText.QrDialogTitle, TutorialText.QrDialogBody),
        new(TutorialStep.GpuDuty, "/", new[] { TutorialRegions.Duty }, true,
            TutorialText.GpuDutyTitle, TutorialText.GpuDutyBody),
        new(TutorialStep.StopAfter, "/settings", new[] { TutorialRegions.SettingStop }, false,
            TutorialText.StopAfterTitle, TutorialText.StopAfterBody),
        new(TutorialStep.NotificationsSetting, "/settings", new[] { TutorialRegions.SettingNotifications }, false,
            TutorialText.NotificationsTitle, TutorialText.NotificationsBody),
        new(TutorialStep.DataFolder, "/settings", new[] { TutorialRegions.SettingData }, false,
            TutorialText.DataFolderTitle, TutorialText.DataFolderBody),
        new(TutorialStep.Done, "/", None, false,
            TutorialText.DoneTitle, TutorialText.DoneBody),
    };
}
