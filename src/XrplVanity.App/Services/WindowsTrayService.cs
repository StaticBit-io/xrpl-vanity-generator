using System.Drawing;
using H.NotifyIcon;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

/// <summary>
/// The notification-area icon. H.NotifyIcon renders the MAUI <see cref="MenuFlyout"/> as a native
/// popup menu, so the menu is rebuilt from scratch on every <see cref="Update"/> — the flyout has
/// no checkable item, and the current duty is marked with a leading tick instead.
/// </summary>
public sealed class WindowsTrayService : ITrayService, IDisposable
{
    private const string IconAssetName = "tray.ico";
    private static readonly int[] DutySteps = { 25, 50, 80, 100 };

    private TaskbarIcon? _icon;
    private Icon? _iconImage;
    private bool _disposed;

    public event Action<TrayAction>? Activated;

    public void Show(TrayMenu menu)
    {
        if (_disposed || _icon is not null)
        {
            Update(menu);
            return;
        }

        _iconImage = LoadIcon();
        _icon = new TaskbarIcon
        {
            ToolTipText = "XRPL Vanity",
            Icon = _iconImage,
            NoLeftClickDelay = true,
            DoubleClickCommand = new Command(() => Raise(TrayAction.Show)),
        };

        ApplyMenu(menu);

        // The icon lives outside the page tree, so it has to be created explicitly. Efficiency
        // mode is off: it lowers the process priority, which would throttle the search itself.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Update(TrayMenu menu)
    {
        if (_disposed || _icon is null)
        {
            return;
        }

        ApplyMenu(menu);
    }

    public void Hide()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Dispose();
        _icon = null;
        _iconImage?.Dispose();
        _iconImage = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
    }

    private void ApplyMenu(TrayMenu menu)
    {
        MenuFlyout flyout = new();

        MenuFlyoutItem show = new() { Text = "Show" };
        show.Clicked += (_, _) => Raise(TrayAction.Show);
        flyout.Add(show);

        MenuFlyoutSubItem duty = new() { Text = "Duty" };
        foreach (int step in DutySteps)
        {
            int value = step;
            MenuFlyoutItem item = new() { Text = $"{(menu.DutyPercent == value ? "✓ " : "    ")}{value} %" };
            item.Clicked += (_, _) => Raise(DutyAction(value));
            duty.Add(item);
        }

        flyout.Add(duty);

        MenuFlyoutItem stop = new() { Text = "Stop search", IsEnabled = menu.SearchRunning };
        stop.Clicked += (_, _) => Raise(TrayAction.StopSearch);
        flyout.Add(stop);

        flyout.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem exit = new() { Text = "Exit" };
        exit.Clicked += (_, _) => Raise(TrayAction.Exit);
        flyout.Add(exit);

        FlyoutBase.SetContextFlyout(_icon, flyout);
    }

    private static TrayAction DutyAction(int percent) => percent switch
    {
        25 => TrayAction.Duty25,
        50 => TrayAction.Duty50,
        80 => TrayAction.Duty80,
        _ => TrayAction.Duty100,
    };

    private void Raise(TrayAction action) => Activated?.Invoke(action);

    private static Icon? LoadIcon()
    {
        // MauiAsset lands next to the executable both unpackaged and inside the MSIX package;
        // the package reader is the fallback. This call still blocks whatever thread calls
        // LoadIcon — Task.Run does not make that free — but it moves the async read itself onto a
        // thread-pool thread with no captured synchronization context, so blocking on its result
        // here cannot deadlock the way an in-place .GetAwaiter().GetResult() on the UI thread could.
        string path = Path.Combine(AppContext.BaseDirectory, IconAssetName);
        try
        {
            if (File.Exists(path))
            {
                return new Icon(path);
            }

            using Stream stream = Task.Run(() => FileSystem.OpenAppPackageFileAsync(IconAssetName)).GetAwaiter().GetResult();
            return new Icon(stream);
        }
        catch (Exception)
        {
            // Without an icon H.NotifyIcon still creates the tray entry; the menu keeps working.
            return null;
        }
    }
}
