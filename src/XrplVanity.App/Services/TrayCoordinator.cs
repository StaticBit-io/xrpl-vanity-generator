using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Services;

/// <summary>
/// Host-side glue between the view model, the tray icon and toast activation. Created once from
/// the Windows platform application after the window exists, so the tray icon has a message loop
/// to attach to.
/// </summary>
public sealed class TrayCoordinator : IDisposable
{
    private readonly SearchViewModel _viewModel;
    private readonly ITrayService _tray;
    private readonly IWindowService _window;
    private readonly WindowsNotificationService _notifications;

    private TrayMenu _current;
    private bool _disposed;

    public TrayCoordinator(SearchViewModel viewModel, ITrayService tray, IWindowService window, WindowsNotificationService notifications)
    {
        _viewModel = viewModel;
        _tray = tray;
        _window = window;
        _notifications = notifications;
        _current = Snapshot();
    }

    public void Start()
    {
        _tray.Activated += OnTrayActivated;
        _viewModel.Changed += OnViewModelChanged;
        _notifications.Activated += OnNotificationActivated;
        _tray.Show(_current);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tray.Activated -= OnTrayActivated;
        _viewModel.Changed -= OnViewModelChanged;
        _notifications.Activated -= OnNotificationActivated;
        _tray.Hide();
    }

    private TrayMenu Snapshot() => new(_viewModel.State != SearchState.Idle, _viewModel.DutyPercent);

    private void OnViewModelChanged()
    {
        // Changed fires on worker threads and on every progress tick; only a menu that actually
        // reads differently is worth rebuilding, and only on the UI thread.
        TrayMenu next = Snapshot();
        if (next == _current || _disposed)
        {
            return;
        }

        _current = next;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_disposed)
            {
                _tray.Update(next);
            }
        });
    }

    private void OnNotificationActivated() => _window.ShowWindow();

    private void OnTrayActivated(TrayAction action)
    {
        switch (action)
        {
            case TrayAction.Show:
                _window.ShowWindow();
                break;
            case TrayAction.Duty25:
                _viewModel.DutyPercent = 25;
                break;
            case TrayAction.Duty50:
                _viewModel.DutyPercent = 50;
                break;
            case TrayAction.Duty80:
                _viewModel.DutyPercent = 80;
                break;
            case TrayAction.Duty100:
                _viewModel.DutyPercent = 100;
                break;
            case TrayAction.StopSearch:
                _viewModel.Stop();
                break;
            case TrayAction.Exit:
                _window.Exit();
                break;
        }
    }
}
