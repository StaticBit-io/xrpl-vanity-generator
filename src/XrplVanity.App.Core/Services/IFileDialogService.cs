namespace XrplVanity.App.Core.Services;

public interface IFileDialogService
{
    /// <summary>Returns the chosen path or null when the user cancelled.</summary>
    Task<string?> PickSaveTsvAsync(string suggestedName);
}
