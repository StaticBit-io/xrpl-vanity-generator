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
            "Take a short tutorial?",
            "You will run one real search and see what to do with the result. Nothing from it is kept. You can start it any time from Settings."),
        new(TutorialStep.AddWord, "/", new[] { TutorialRegions.Patterns }, true,
            "Add a word",
            "Type a word of ten letters or more and press Enter."),
        new(TutorialStep.ListFiles, "/", new[] { TutorialRegions.Patterns }, true,
            "Keep your list",
            "Save list writes your words to a file. Load list adds them back later."),
        new(TutorialStep.Devices, "/", new[] { TutorialRegions.Devices }, true,
            "Choose what searches",
            "Tick the CPU and any graphics cards, then press measure speed. The estimate needs that number."),
        new(TutorialStep.LongWord, "/", new[] { TutorialRegions.Patterns, TutorialRegions.Estimate }, true,
            "Check the time",
            "This is how long a match should take. If it looks quick, replace your word with one of ten letters or more."),
        new(TutorialStep.ShortWord, "/", new[] { TutorialRegions.Patterns, TutorialRegions.Estimate }, true,
            "Make it shorter",
            "Every extra character multiplies the wait by up to 58. Add a word of two or three letters."),
        new(TutorialStep.Search, "/", new[] { TutorialRegions.Start, TutorialRegions.Patterns, TutorialRegions.Estimate, TutorialRegions.Stats, TutorialRegions.Results }, true,
            "Start the search",
            "Press Start. The search stops at the first match. If it runs too long, stop it and shorten the word."),
        new(TutorialStep.Result, "/", new[] { TutorialRegions.Results }, false,
            "This is your result",
            "The seed controls this address, and whoever has it controls the account. Keep a real seed secret. This practice result disappears when the tutorial ends."),
        new(TutorialStep.QrButton, "/", new[] { TutorialRegions.Results }, true,
            "Move it to a wallet",
            "Press the QR button on the result."),
        new(TutorialStep.QrDialog, "/", None, false,
            "Scan it with a wallet",
            "With a real result, you would show this code and scan it with a wallet on your phone. You do not need to scan this one. Close the dialog to continue."),
        new(TutorialStep.GpuDuty, "/", new[] { TutorialRegions.Duty }, true,
            "Set the load",
            "A lower value leaves the card free for other applications."),
        new(TutorialStep.StopAfter, "/settings", new[] { TutorialRegions.SettingStop }, false,
            "Stop after N matches",
            "A search runs until you press Stop. A number here ends it by itself."),
        new(TutorialStep.NotificationsSetting, "/settings", new[] { TutorialRegions.SettingNotifications }, false,
            "Notifications",
            "A notification tells you about each match and when a search finishes."),
        new(TutorialStep.DataFolder, "/settings", new[] { TutorialRegions.SettingData }, false,
            "Your data",
            "Your results and settings are kept in this folder."),
        new(TutorialStep.Done, "/", None, false,
            "You are ready",
            "Finish puts your words and settings back as they were. You can start this tutorial again in Settings."),
    };
}
