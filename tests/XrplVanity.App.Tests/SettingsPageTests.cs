using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class SettingsPageTests : BunitContext
{
    [Fact]
    public void Changing_A_Setting_Saves_It_Immediately()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        cut.Find("select.close-behavior").Change("Exit");
        Assert.Equal(CloseBehavior.Exit, settings.Current.CloseBehavior);

        cut.Find("input.clipboard-seconds").Change(45);
        Assert.Equal(45, settings.Current.ClipboardClearSeconds);

        cut.Find("button.forget-hints").Click();
        Assert.Empty(settings.Current.RateHints);
    }

    private sealed class NullWindowService : IWindowService
    {
#pragma warning disable CS0067 // Required by IWindowService; this fake never raises it.
        public event Action? CloseRequested;
#pragma warning restore CS0067
        public void ShowWindow() { }
        public void HideToTray() { }
        public void Exit() { }
        public string DataFolder => "C:\\data";
        public void OpenDataFolder() { }
    }
}
