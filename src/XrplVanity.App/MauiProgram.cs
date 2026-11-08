using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.App.Services;
using XrplVanity.Search;

namespace XrplVanity.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.UseNotifyIcon();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        string dataFolder = FileSystem.AppDataDirectory;
        builder.Services.AddSingleton<ISettingsStore>(new JsonSettingsStore(dataFolder));
        builder.Services.AddSingleton<IResultStore>(new DpapiResultStore(dataFolder));
        builder.Services.AddSingleton<WindowsNotificationService>();
        builder.Services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<WindowsNotificationService>());
        builder.Services.AddSingleton<IClipboardService, WindowsClipboardService>();
        builder.Services.AddSingleton<IFileDialogService, WindowsFileDialogService>();
        builder.Services.AddSingleton<WindowsWindowService>();
        builder.Services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowsWindowService>());
        builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(sp => new SearchViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IResultStore>(),
            sp.GetRequiredService<INotificationService>(),
            DeviceCatalog.Enumerate));
        builder.Services.AddSingleton(sp => new TutorialService(sp.GetRequiredService<SearchViewModel>(), sp.GetRequiredService<IResultStore>()));
        builder.Services.AddSingleton<TrayCoordinator>();

        MauiApp app = builder.Build();

        // The view model's constructor enumerates OpenCL/CUDA devices, which blocks for seconds on
        // the first call. Warm the singleton on a background thread while WinUI and the WebView
        // start, so the first Blazor render finds it built instead of paying that cost on the UI
        // thread. Concurrent resolution from the UI thread simply waits for this one to finish.
        _ = Task.Run(() =>
        {
            try
            {
                app.Services.GetRequiredService<SearchViewModel>();
            }
            catch (Exception)
            {
                // The UI resolves the view model again on the first render and surfaces the failure there.
            }
        });

        return app;
    }
}
