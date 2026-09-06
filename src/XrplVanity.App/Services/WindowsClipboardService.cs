using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingClear;
    private string? _pendingSecret;

    public Task CopyAsync(string text)
    {
        CancelPendingClear();
        return MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
    }

    public async Task CopySecretAsync(string text, TimeSpan clearAfter)
    {
        CancellationTokenSource cts = new();
        lock (_gate)
        {
            _pendingClear?.Cancel();
            _pendingClear?.Dispose();
            _pendingClear = cts;
            _pendingSecret = text;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
        }
        catch (Exception)
        {
            // The copy never landed, so there is nothing to clear: drop the timer rather than
            // leaving a cancellation source that nobody will ever cancel or dispose.
            lock (_gate)
            {
                if (ReferenceEquals(_pendingClear, cts))
                {
                    _pendingClear = null;
                    _pendingSecret = null;
                }
            }

            cts.Cancel();
            cts.Dispose();
            throw;
        }

        _ = ClearLaterAsync(text, clearAfter, cts);
    }

    /// <summary>
    /// Clears the clipboard immediately if it still holds the last copied seed. The delayed clear
    /// is an in-process <c>Task.Delay</c>, so without this a seed copied seconds before the user
    /// exits would outlive the application on the clipboard.
    /// </summary>
    public async Task ClearPendingSecretAsync()
    {
        string? secret;
        lock (_gate)
        {
            secret = _pendingSecret;
            _pendingClear?.Cancel();
            _pendingClear?.Dispose();
            _pendingClear = null;
            _pendingSecret = null;
        }

        if (secret is null)
        {
            return;
        }

        try
        {
            string? current = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
            if (string.Equals(current, secret, StringComparison.Ordinal))
            {
                await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(string.Empty));
            }
        }
        catch (Exception)
        {
            // The clipboard can be locked by another application; clearing it is best effort and
            // must never hold up or fault the exit path.
        }
    }

    private void CancelPendingClear()
    {
        lock (_gate)
        {
            _pendingClear?.Cancel();
            _pendingClear?.Dispose();
            _pendingClear = null;
            _pendingSecret = null;
        }
    }

    private async Task ClearLaterAsync(string text, TimeSpan delay, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token);
            string? current = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
            if (string.Equals(current, text, StringComparison.Ordinal))
            {
                await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(string.Empty));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer copy superseded this one.
        }
        catch (Exception)
        {
            // The clipboard can be held by another app (locked, or owned by a clipboard viewer);
            // clearing it is best effort and must never become an unobserved task exception.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingClear, cts))
                {
                    _pendingClear = null;
                    _pendingSecret = null;
                }
            }

            cts.Dispose();
        }
    }
}
