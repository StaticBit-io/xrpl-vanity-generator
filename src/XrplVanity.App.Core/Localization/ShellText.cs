using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The navigation rail and the close prompt.</summary>
public static class ShellText
{
    /// <summary>Shown for an address that no page answers.</summary>
    public static readonly TextId NotFound = Id();
    public static readonly TextId NavSearch = Id();
    public static readonly TextId NavHistory = Id();
    public static readonly TextId NavSettings = Id();
    public static readonly TextId ClosePromptTitle = Id();
    public static readonly TextId ClosePromptBody = Id();
    public static readonly TextId RememberChoice = Id();

    /// <summary>Also the label of the matching option on the Settings page.</summary>
    public static readonly TextId MinimiseToTray = Id();

    /// <summary>Also the label of the matching option on the Settings page.</summary>
    public static readonly TextId Exit = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Shell", name);
}
