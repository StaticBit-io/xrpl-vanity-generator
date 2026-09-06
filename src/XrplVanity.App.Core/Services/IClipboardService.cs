namespace XrplVanity.App.Core.Services;

public interface IClipboardService
{
    Task CopyAsync(string text);

    /// <summary>Copies a secret and clears the clipboard after <paramref name="clearAfter"/> if it still holds the same text.</summary>
    Task CopySecretAsync(string text, TimeSpan clearAfter);

    /// <summary>
    /// Clears the clipboard now if it still holds the last copied secret and cancels the pending
    /// clear; a no-op otherwise. The delayed clear is an in-process timer, so exiting inside the
    /// delay would otherwise leave a seed on the clipboard after the application is gone.
    /// </summary>
    Task ClearPendingSecretAsync();
}
