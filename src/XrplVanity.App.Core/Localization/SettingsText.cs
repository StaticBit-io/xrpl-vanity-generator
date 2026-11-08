using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The Settings screen.</summary>
public static class SettingsText
{
    public static readonly TextId LanguageTitle = Id();
    public static readonly TextId LanguageLabel = Id();
    public static readonly TextId LanguageNotRemembered = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Settings", name);
}
