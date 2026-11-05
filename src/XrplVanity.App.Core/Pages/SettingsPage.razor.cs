using System.Reflection;
using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Pages;

public partial class SettingsPage
{
    private const int MinSeconds = 1;
    private const int MaxSeconds = 3600;

    /// <summary>The same range the Search page's duty slider offers.</summary>
    private const int MinDuty = 25;
    private const int MaxDuty = 100;

    /// <summary>
    /// The product version, read from the assembly rather than written into the markup, so that a
    /// bump of VanityVersion in Directory.Build.props reaches the About line as well. The
    /// informational version carries a "+&lt;commit sha&gt;" suffix that the UI has no use for.
    /// </summary>
    private static readonly string AppVersion =
        typeof(SettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    /// <summary>
    /// The tutorial forces a stop count of 1 on the live view model so its search ends at the first
    /// match; the field must show the user's own value, which is what the saved settings still hold.
    /// </summary>
    private int? ShownCount => Vm.IsSandbox ? Vm.Settings.Count : Vm.Count;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        // CanStart also turns false while the offer card is open and true again when it is answered,
        // which the view model does not announce.
        Tutorial.Changed += OnViewModelChanged;
    }

    public override void Dispose()
    {
        Tutorial.Changed -= OnViewModelChanged;
        base.Dispose();
    }

    private void StartTutorial() => Tutorial.Begin();

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
            Vm.DutyPercent = Math.Clamp(value, MinDuty, MaxDuty);
        }
    }

    /// <summary>Blank or 0 means unbounded; the view model's Count setter maps anything below 1 to null.</summary>
    private void OnCountChanged(ChangeEventArgs e)
    {
        string? raw = (string?)e.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            Vm.Count = null;
            return;
        }

        if (int.TryParse(raw, out int value))
        {
            Vm.Count = value;
        }
    }

    private void OnClipboardSecondsChanged(ChangeEventArgs e)
    {
        if (int.TryParse((string?)e.Value, out int value))
        {
            Vm.SaveSettings(s => s with { ClipboardClearSeconds = Math.Clamp(value, MinSeconds, MaxSeconds) });
        }
    }

    private void OnRevealSecondsChanged(ChangeEventArgs e)
    {
        if (int.TryParse((string?)e.Value, out int value))
        {
            Vm.SaveSettings(s => s with { SeedRevealSeconds = Math.Clamp(value, MinSeconds, MaxSeconds) });
        }
    }

    private void ForgetHints() =>
        Vm.SaveSettings(s => s with { RateHints = new Dictionary<string, double>() });
}
