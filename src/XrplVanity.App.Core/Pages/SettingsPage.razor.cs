using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Pages;

public partial class SettingsPage
{
    private void OnCloseBehaviorChanged(ChangeEventArgs e)
    {
        if (Enum.TryParse((string?)e.Value, out CloseBehavior parsed))
        {
            Vm.SaveSettings(s => s with { CloseBehavior = parsed });
        }
    }

    private void OnNotificationsChanged(ChangeEventArgs e) =>
        Vm.SaveSettings(s => s with { NotificationsEnabled = (bool)e.Value! });

    private void OnDefaultDutyChanged(ChangeEventArgs e)
    {
        if (int.TryParse((string?)e.Value, out int value))
        {
            Vm.DutyPercent = value;
        }
    }

    private void OnClipboardSecondsChanged(ChangeEventArgs e)
    {
        if (int.TryParse((string?)e.Value, out int value))
        {
            Vm.SaveSettings(s => s with { ClipboardClearSeconds = value });
        }
    }

    private void OnRevealSecondsChanged(ChangeEventArgs e)
    {
        if (int.TryParse((string?)e.Value, out int value))
        {
            Vm.SaveSettings(s => s with { SeedRevealSeconds = value });
        }
    }

    private void ForgetHints() =>
        Vm.SaveSettings(s => s with { RateHints = new Dictionary<string, double>() });
}
