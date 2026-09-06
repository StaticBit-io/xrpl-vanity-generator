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

    [Fact]
    public void Clipboard_And_Reveal_Seconds_Are_Clamped_To_A_Sane_Range()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        cut.Find("input.clipboard-seconds").Change(-5);
        Assert.Equal(1, settings.Current.ClipboardClearSeconds);

        cut.Find("input.clipboard-seconds").Change(999999);
        Assert.Equal(3600, settings.Current.ClipboardClearSeconds);

        cut.Find("input.reveal-seconds").Change(-5);
        Assert.Equal(1, settings.Current.SeedRevealSeconds);

        cut.Find("input.reveal-seconds").Change(999999);
        Assert.Equal(3600, settings.Current.SeedRevealSeconds);
    }

    [Fact]
    public void The_Match_Count_Input_Sets_A_Limit_And_Blank_Means_Unbounded()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        cut.Find("input.match-count").Change(25);
        Assert.Equal(25, vm.Count);
        Assert.Equal(25, settings.Current.Count);

        cut.Find("input.match-count").Change(string.Empty);
        Assert.Null(vm.Count);
        Assert.Null(settings.Current.Count);

        cut.Find("input.match-count").Change(0);
        Assert.Null(vm.Count);
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
