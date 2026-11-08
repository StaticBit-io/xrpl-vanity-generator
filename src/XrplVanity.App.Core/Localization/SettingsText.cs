using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The Settings screen.</summary>
public static class SettingsText
{
    public static readonly TextId LanguageTitle = Id();
    public static readonly TextId LanguageLabel = Id();
    public static readonly TextId LanguageNotRemembered = Id();

    public static readonly TextId Title = Id();
    public static readonly TextId AutoSave = Id();
    public static readonly TextId WindowTitle = Id();
    public static readonly TextId CloseButtonLabel = Id();
    public static readonly TextId CloseButtonDesc = Id();
    public static readonly TextId CloseAsk = Id();
    public static readonly TextId NotificationsLabel = Id();
    public static readonly TextId NotificationsDesc = Id();
    public static readonly TextId NotificationsDescUnavailable = Id();
    public static readonly TextId GpuDutyLabel = Id();
    public static readonly TextId GpuDutyDesc = Id();
    public static readonly TextId StopAfterLabel = Id();
    public static readonly TextId StopAfterDesc = Id();
    public static readonly TextId SecretsTitle = Id();
    public static readonly TextId ClipboardLabel = Id();
    public static readonly TextId ClipboardDesc = Id();
    public static readonly TextId RevealLabel = Id();
    public static readonly TextId RevealDesc = Id();
    public static readonly TextId RateHintsLabel = Id();
    public static readonly TextId RateHintsDesc = Id();
    public static readonly TextId ForgetRateHints = Id();
    public static readonly TextId DataTitle = Id();
    public static readonly TextId DataFolderLabel = Id();
    public static readonly TextId OpenFolder = Id();
    public static readonly TextId TutorialLabel = Id();
    public static readonly TextId TutorialDesc = Id();
    public static readonly TextId StartTutorial = Id();
    public static readonly TextId AboutTitle = Id();
    /// <summary>Value: <c>version</c>. The product and font names in it are not translated.</summary>
    public static readonly TextId AboutDescription = Id();
    public static readonly TextId ProjectOnGitHub = Id();
    public static readonly TextId StaticBitOnGitHub = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Settings", name);
}
