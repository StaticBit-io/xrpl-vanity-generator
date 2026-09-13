namespace XrplVanity.App.Core.Services;

public interface IFileDialogService
{
    /// <summary>
    /// Shows the platform's save dialog. <paramref name="label"/> names the file type the dialog
    /// offers ("Tab-separated values"), <paramref name="extension"/> is its dotted suffix (".tsv").
    /// Returns the chosen path or null when the user cancelled.
    /// </summary>
    Task<string?> PickSaveAsync(string suggestedName, string label, string extension);
}
