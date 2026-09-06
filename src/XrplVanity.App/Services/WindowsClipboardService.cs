using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    private CancellationTokenSource? _pendingClear;

    public Task CopyAsync(string text)
    {
        _pendingClear?.Cancel();
        return Clipboard.Default.SetTextAsync(text);
    }

    public async Task CopySecretAsync(string text, TimeSpan clearAfter)
    {
        _pendingClear?.Cancel();
        CancellationTokenSource cts = new();
        _pendingClear = cts;
        await Clipboard.Default.SetTextAsync(text);
        _ = ClearLaterAsync(text, clearAfter, cts.Token);
    }

    private static async Task ClearLaterAsync(string text, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            string? current = await Clipboard.Default.GetTextAsync();
            if (string.Equals(current, text, StringComparison.Ordinal))
            {
                await Clipboard.Default.SetTextAsync(string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer copy superseded this one.
        }
    }
}
