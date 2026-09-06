using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingClear;

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
        }

        await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
        _ = ClearLaterAsync(text, clearAfter, cts);
    }

    private void CancelPendingClear()
    {
        lock (_gate)
        {
            _pendingClear?.Cancel();
            _pendingClear?.Dispose();
            _pendingClear = null;
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
                }
            }

            cts.Dispose();
        }
    }
}
