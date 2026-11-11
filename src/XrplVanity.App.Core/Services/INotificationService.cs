using XrplVanity.App.Core.Localization;

namespace XrplVanity.App.Core.Services;

public interface INotificationService
{
    /// <summary>Shows a Windows notification; a no-op when notifications are unavailable.</summary>
    void Notify(UiText title, UiText body);
    bool IsAvailable { get; }
}
