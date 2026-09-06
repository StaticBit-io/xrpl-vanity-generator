namespace XrplVanity.App.Core.Services;

public interface INotificationService
{
    /// <summary>Shows a Windows notification; a no-op when notifications are unavailable.</summary>
    void Notify(string title, string body);
    bool IsAvailable { get; }
}
