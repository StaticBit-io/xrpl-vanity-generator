namespace XrplVanity.App.Core.Services;

public enum TrayAction { Show, Duty25, Duty50, Duty80, Duty100, StopSearch, Exit }

public sealed record TrayMenu(bool SearchRunning, int DutyPercent, string LanguageCode);

public interface ITrayService
{
    void Show(TrayMenu menu);
    void Update(TrayMenu menu);
    void Hide();
    event Action<TrayAction>? Activated;
}
