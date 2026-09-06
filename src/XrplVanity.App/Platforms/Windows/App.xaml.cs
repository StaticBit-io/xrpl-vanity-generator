using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using XrplVanity.App.Services;

namespace XrplVanity.App.WinUI;

/// <summary>
/// The WinUI entry point. MAUI creates the platform window inside base.OnLaunched, so everything
/// that needs the real window — the saved size, the minimum size, the close-button interception
/// and the tray icon — is wired up immediately afterwards.
/// </summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        IServiceProvider? services = IPlatformApplication.Current?.Services;
        Microsoft.Maui.Controls.Window? mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (services is null || mauiWindow is null)
        {
            return;
        }

        if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow)
        {
            AttachWindow(services, platformWindow);
            return;
        }

        // The handler is created a beat later on some activation paths; take the window then.
        void OnHandlerChanged(object? sender, EventArgs e)
        {
            mauiWindow.HandlerChanged -= OnHandlerChanged;
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window created)
            {
                AttachWindow(services, created);
            }
        }

        mauiWindow.HandlerChanged += OnHandlerChanged;
    }

    private static void AttachWindow(IServiceProvider services, Microsoft.UI.Xaml.Window window)
    {
        services.GetRequiredService<WindowsWindowService>().Attach(window);

        // The coordinator pulls in the view model, whose constructor enumerates GPU devices; it is
        // resolved off the UI thread so a slow OpenCL driver cannot delay the first paint.
        _ = Task.Run(() =>
        {
            try
            {
                TrayCoordinator coordinator = services.GetRequiredService<TrayCoordinator>();
                MainThread.BeginInvokeOnMainThread(coordinator.Start);
            }
            catch (Exception)
            {
                // A missing tray (e.g. GPU enumeration failure while resolving the view model) must
                // not crash start-up; the window and the search itself still work without it.
            }
        });
    }
}
