using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The notification-area menu. Its "Exit" is <see cref="ShellText.Exit"/>.</summary>
public static class TrayText
{
    public static readonly TextId Show = Id();
    public static readonly TextId Duty = Id();

    /// <summary>One duty step; value: <c>percent</c>. The current step's tick is added by the menu.</summary>
    public static readonly TextId DutyStep = Id();
    public static readonly TextId StopSearch = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Tray", name);
}
