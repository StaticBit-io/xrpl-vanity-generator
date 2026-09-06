namespace XrplVanity.App.Core.Services;

public interface IClipboardService
{
    Task CopyAsync(string text);

    /// <summary>Copies a secret and clears the clipboard after <paramref name="clearAfter"/> if it still holds the same text.</summary>
    Task CopySecretAsync(string text, TimeSpan clearAfter);
}
