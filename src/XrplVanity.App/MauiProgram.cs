using System.Globalization;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using XrplVanity.App.Core.Localization;
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
        PinWebView2ProfileForUnpackagedBuild(dataFolder);
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
        builder.Services.AddSingleton(sp => new Localizer(
            sp.GetRequiredService<ISettingsStore>().Load().Language,
            CultureInfo.CurrentUICulture));
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

    /// <summary>
    /// WebView2 keeps its profile next to the executable unless told otherwise. The ZIP build can be
    /// unpacked anywhere, including Program Files, which a normal user cannot write to: WebView2 then
    /// reports that it cannot create its data directory and the window stays empty. The profile goes
    /// beside the data folder instead. BlazorWebViewInitializingEventArgs.UserDataFolder cannot do
    /// this on Windows: MAUI 10.0.110 builds an environment from it and then calls
    /// EnsureCoreWebView2Async() without that environment, so the setting is dropped. The WebView2
    /// loader reads this variable when it creates the default environment. A packaged build already
    /// keeps the profile in the package's local state, and a value set outside the process wins.
    /// </summary>
    private static void PinWebView2ProfileForUnpackagedBuild(string dataFolder)
    {
        const string variable = "WEBVIEW2_USER_DATA_FOLDER";
        if (AppInfo.Current.PackagingModel != AppPackagingModel.Unpackaged
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
        {
            return;
        }

        string appFolder = Path.GetDirectoryName(dataFolder)!;
        Environment.SetEnvironmentVariable(variable, Path.Combine(appFolder, "WebView2"));
    }
}
