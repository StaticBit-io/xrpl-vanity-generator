using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Services;

/// <summary>
/// Owns the single WinUI window: restores the saved size, keeps it above the 900x600 floor,
/// turns the title bar's close button into <see cref="CloseRequested"/> instead of an exit, and
/// persists the size back through the view model (never straight to the store, or the view
/// model's next write would clobber it with its stale copy of the settings).
/// </summary>
public sealed class WindowsWindowService : IWindowService, IDisposable
{
    private const int MinimumWidth = 900;
    private const int MinimumHeight = 600;
    private const int GwlpWndProc = -4;
    private const uint WmGetMinMaxInfo = 0x0024;
    private static readonly TimeSpan SaveSizeDebounce = TimeSpan.FromMilliseconds(500);

    private readonly IServiceProvider _services;
    private readonly ISettingsStore _settingsStore;
    private readonly Timer _saveSizeTimer;
    private readonly object _gate = new();

    private Microsoft.UI.Xaml.Window? _window;
    private AppWindow? _appWindow;
    private IntPtr _handle;
    private IntPtr _previousWndProc;
    private WndProc? _wndProc;
    private SizeInt32? _pendingSize;
    private bool _disposed;
    private bool _exiting;

    public WindowsWindowService(IServiceProvider services, ISettingsStore settingsStore)
    {
        _services = services;
        _settingsStore = settingsStore;
        _saveSizeTimer = new Timer(_ => FlushPendingSize(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action? CloseRequested;

    public string DataFolder => FileSystem.AppDataDirectory;

    /// <summary>The window handle, for APIs that need an owner (the file picker).</summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Binds the WinUI window created by MAUI. Called once from the Windows platform application
    /// after base.OnLaunched, when the handler has produced the platform window.
    /// </summary>
    public void Attach(Microsoft.UI.Xaml.Window window)
    {
        _window = window;
        _appWindow = window.AppWindow;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);

        AppSettings settings = _settingsStore.Load();
        // Sizes are stored as the whole window in logical pixels — the unit the mockups use — and
        // every AppWindow API works in physical pixels, hence the DPI scale. Window size rather
        // than client size because MAUI extends its content into the title bar, which makes
        // ResizeClient and ClientSize disagree by the title bar's height; going through
        // Resize/Size keeps save and restore in exactly the same unit, so the window cannot creep
        // a title bar taller on every launch.
        double scale = GetDpiForWindow(_handle) / 96.0;
        int width = (int)Math.Round(Math.Max(settings.WindowWidth, MinimumWidth) * scale);
        int height = (int)Math.Round(Math.Max(settings.WindowHeight, MinimumHeight) * scale);

        // A default that is generous on a large monitor must not hang off a small one. This
        // happens before the Changed subscription below, so neither the clamp nor the restore is
        // mistaken for a user resize and written back over the saved size.
        RectInt32 workArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

        InstallMinimumSizeHook();

        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;
    }

    public void ShowWindow()
    {
        if (_appWindow is null || _window is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _appWindow.Show();
            if (_appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            _window.Activate();
            H.NotifyIcon.Core.WindowUtilities.SetForegroundWindow(_handle);
        });
    }

    public void HideToTray()
    {
        if (_appWindow is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => _appWindow.Hide());
    }

    public void Exit()
    {
        lock (_gate)
        {
            if (_exiting)
            {
                // Already tearing down (a second close, a second tray "Exit"); do nothing more.
                return;
            }

            _exiting = true;
        }

        FlushPendingSize();

        // Stop the search before anything tears the process down: the session owns worker threads
        // and GPU contexts, and Quit() does not unwind them.
        _services.GetService<SearchViewModel>()?.Dispose();
        _services.GetService<ITrayService>()?.Hide();
        _services.GetService<WindowsNotificationService>()?.Shutdown();

        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    }

    public void OpenDataFolder()
    {
        string folder = DataFolder;
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // Opening the folder is best effort; the button must never crash the app.
        }
    }

    /// <summary>Raised when the title bar's close button is pressed.</summary>
    public void RaiseCloseRequested()
    {
        Action? handler = CloseRequested;
        if (handler is null)
        {
            // The layout has not subscribed yet — during the first seconds the WebView is still
            // loading. The close button must still close rather than doing nothing at all.
            Exit();
            return;
        }

        handler.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveSizeTimer.Dispose();
        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow.Changed -= OnAppWindowChanged;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting)
        {
            // This close was raised by Exit()'s own Application.Current.Quit() — let it proceed.
            // Cancelling it here would leave a zombie process: no window, no tray, unkillable.
            return;
        }

        // The window never closes itself: MainLayout decides between hiding and exiting.
        args.Cancel = true;
        RaiseCloseRequested();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _appWindow is null)
        {
            return;
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter && presenter.State != OverlappedPresenterState.Restored)
        {
            // A maximised or minimised window is not the size to come back to.
            return;
        }

        lock (_gate)
        {
            _pendingSize = _appWindow.Size;
        }

        if (!_disposed)
        {
            _saveSizeTimer.Change(SaveSizeDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushPendingSize()
    {
        SizeInt32 size;
        lock (_gate)
        {
            if (_pendingSize is not SizeInt32 pending)
            {
                return;
            }

            _pendingSize = null;
            size = pending;
        }

        double scale = _handle == IntPtr.Zero ? 1.0 : GetDpiForWindow(_handle) / 96.0;
        int width = Math.Max(MinimumWidth, (int)Math.Round(size.Width / scale));
        int height = Math.Max(MinimumHeight, (int)Math.Round(size.Height / scale));

        // Through the view model so its in-memory settings stay authoritative.
        SearchViewModel? viewModel = _services.GetService<SearchViewModel>();
        viewModel?.SaveSettings(s => s with { WindowWidth = width, WindowHeight = height });
    }

    private void InstallMinimumSizeHook()
    {
        // WM_GETMINMAXINFO is the documented way to give a window a minimum track size; the
        // delegate is kept in a field so the GC cannot collect it while the window proc points at it.
        _wndProc = MinimumSizeWndProc;

        // SetWindowLongPtrW can legitimately return zero on success (if the previous wndproc
        // pointer itself was zero), so per the documented pattern, clear the last error first and
        // treat a zero return as failure only when GetLastPInvokeError is also non-zero.
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetWindowLongPtrW(_handle, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            // Hooking the window proc failed. Leave the window without an enforced floor rather
            // than routing messages through a null previous proc, which would crash on the next
            // window message.
            _wndProc = null;
            return;
        }

        _previousWndProc = previous;
    }

    private IntPtr MinimumSizeWndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            // Recomputed from the window's current DPI on every call, not the DPI captured at
            // Attach() time, so the 900x600 logical floor holds after the window moves to a
            // monitor with a different scale factor.
            double scale = GetDpiForWindow(hwnd) / 96.0;
            int minTrackWidth = (int)Math.Round(MinimumWidth * scale);
            int minTrackHeight = (int)Math.Round(MinimumHeight * scale);

            MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = Math.Max(info.MinTrackSize.X, minTrackWidth);
            info.MinTrackSize.Y = Math.Max(info.MinTrackSize.Y, minTrackHeight);
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }

        return CallWindowProcW(_previousWndProc, hwnd, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
