namespace XrplVanity.App.Core.Services;

public enum CloseBehavior { Ask, MinimizeToTray, Exit }

public interface IWindowService
{
    void ShowWindow();
    void HideToTray();
    void Exit();

    /// <summary>The folder where settings and results are persisted.</summary>
    string DataFolder { get; }

    /// <summary>Opens <see cref="DataFolder"/> in the platform file explorer.</summary>
    void OpenDataFolder();

    /// <summary>Raised when the user presses the window's close button; the handler decides what happens.</summary>
    event Action? CloseRequested;
}
