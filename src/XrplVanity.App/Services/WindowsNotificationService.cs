using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

/// <summary>
/// Windows App SDK toasts. <see cref="Activated"/> is deliberately not on
/// <see cref="INotificationService"/>: only the host cares that a toast was clicked, and
/// <see cref="TrayCoordinator"/> subscribes to this concrete type to bring the window back.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private readonly Localizer _localizer;

    public WindowsNotificationService(Localizer localizer)
    {
        _localizer = localizer;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => Activated?.Invoke();
            AppNotificationManager.Default.Register();
            IsAvailable = true;
        }
        catch (Exception)
        {
            // Unpackaged runs without a registered identity cannot show toasts, and a packaged run
            // whose manifest declares no toast activator is refused with "No COM servers are
            // registered for this app"; the UI already shows every match, so the app stays fully
            // usable without them.
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; }

    public event Action? Activated;

    /// <summary>
    /// Unregisters from the notification platform. Called from <see cref="WindowsWindowService.Exit"/>
    /// before <c>Application.Current.Quit()</c> so the process does not leave a dangling registration.
    /// </summary>
    public void Shutdown()
    {
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception)
        {
            // Best effort during shutdown; the process is exiting either way.
        }
    }

    public void Notify(UiText title, UiText body)
    {
        if (!IsAvailable)
        {
            return;
        }

        // Rendered at call time so a toast raised after a language change speaks the new language.
        TextCatalog text = _localizer.Text;
        try
        {
            AppNotification notification = new AppNotificationBuilder().AddText(text.Render(title)).AddText(text.Render(body)).BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception)
        {
            // Notifications are best effort; the UI already shows the result.
        }
    }
}
