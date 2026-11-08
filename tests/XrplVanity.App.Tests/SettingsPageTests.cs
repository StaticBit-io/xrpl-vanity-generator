using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Tutorial;
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
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
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
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
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
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
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

    [Fact]
    public void The_About_Area_Links_To_The_Project_And_StaticBit()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        IReadOnlyDictionary<string, string> hrefsByText = cut.FindAll(".about-links a")
            .ToDictionary(a => a.TextContent.Trim(), a => a.GetAttribute("href") ?? string.Empty);

        Assert.Equal("https://github.com/StaticBit-io/xrpl-vanity-generator", hrefsByText["Project on GitHub"]);
        Assert.Equal("https://github.com/StaticBit-io", hrefsByText["StaticBit on GitHub"]);
        Assert.Equal("https://x.com/StaticBit_io", hrefsByText["X"]);
        Assert.Equal("https://staticbit.io/", hrefsByText["staticbit.io"]);
    }

    [Fact]
    public void Every_Setting_Row_Sits_In_A_Region()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        string RegionOf(string selector) => cut.Find(selector).Closest("[data-tutorial]")!.GetAttribute("data-tutorial")!;

        Assert.Equal(TutorialRegions.SettingStop, RegionOf("input.match-count"));
        Assert.Equal(TutorialRegions.SettingNotifications, RegionOf("input.notifications"));
        Assert.Equal(TutorialRegions.SettingData, RegionOf("button.open-folder"));
        Assert.Equal(TutorialRegions.SettingsOther, RegionOf("select.close-behavior"));

        foreach (AngleSharp.Dom.IElement row in cut.FindAll(".srow"))
        {
            Assert.NotNull(row.Closest("[data-tutorial]"));
            // One row per region: a wrapper holding two rows would light or dim both.
            Assert.Single(row.Closest("[data-tutorial]")!.QuerySelectorAll(".srow"));
        }
    }

    [Fact]
    public void Start_Tutorial_Is_Disabled_While_A_Search_Runs()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        TutorialService tutorial = new(vm, new MemoryResultStore());
        Services.AddSingleton(vm);
        Services.AddSingleton(tutorial);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();
        Assert.False(cut.Find("button.run-tutorial").HasAttribute("disabled"));

        vm.AddPattern("zzzzzzzz", XrplVanity.Core.MatchPosition.Starts, false);
        vm.Start();
        try
        {
            // The page re-renders from Vm.Changed, so the button follows the state without a click.
            cut.WaitForAssertion(() => Assert.True(cut.Find("button.run-tutorial").HasAttribute("disabled")), TimeSpan.FromSeconds(30));
            Assert.False(tutorial.CanStart);
        }
        finally
        {
            vm.Stop();
        }

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.run-tutorial").HasAttribute("disabled")), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Start_Tutorial_Begins_It()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        TutorialService tutorial = new(vm, new MemoryResultStore());
        Services.AddSingleton(vm);
        Services.AddSingleton(tutorial);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        cut.Find("button.run-tutorial").Click();

        Assert.True(tutorial.IsActive);
        Assert.True(vm.IsSandbox);
        Assert.Equal(TutorialStep.AddWord, tutorial.Current!.Step);
        cut.WaitForAssertion(() => Assert.True(cut.Find("button.run-tutorial").HasAttribute("disabled")));
    }

    [Fact]
    public void Stop_After_Shows_The_Users_Value_During_The_Tutorial()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        vm.Count = 7;
        TutorialService tutorial = new(vm, new MemoryResultStore());
        Services.AddSingleton(vm);
        Services.AddSingleton(tutorial);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();
        Assert.Equal("7", cut.Find("input.match-count").GetAttribute("value"));

        Assert.True(tutorial.Begin());

        // The sandbox stops at the first match, so the live count is 1; the field is the user's setting.
        Assert.Equal(1, vm.Count);
        cut.WaitForAssertion(() => Assert.Equal("7", cut.Find("input.match-count").GetAttribute("value")));
    }

    [Fact]
    public void Start_Tutorial_Sits_In_Its_Own_Region()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        AngleSharp.Dom.IElement region = cut.Find("button.run-tutorial").Closest("[data-tutorial]")!;

        Assert.Equal(TutorialRegions.SettingsOther, region.GetAttribute("data-tutorial"));
        Assert.Single(region.QuerySelectorAll(".srow"));
        Assert.Equal("Start tutorial", cut.Find("button.run-tutorial").TextContent.Trim());
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
