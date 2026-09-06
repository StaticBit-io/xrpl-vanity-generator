using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Layout;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class MainLayoutTests : BunitContext
{
    private (SearchViewModel Vm, FakeWindowService Window) Register()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        FakeWindowService window = new();
        Services.AddSingleton(vm);
        Services.AddSingleton<IWindowService>(window);
        return (vm, window);
    }

    [Fact]
    public void Disposing_Unsubscribes_From_CloseRequested()
    {
        (SearchViewModel _, FakeWindowService window) = Register();
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();
        Assert.Equal(1, window.SubscriberCount);

        cut.Instance.Dispose();

        Assert.Equal(0, window.SubscriberCount);
        window.RaiseCloseRequested();   // must be a no-op now: no exception, no prompt shown, no tray/exit call
        Assert.False(window.HideToTrayCalled);
        Assert.False(window.ExitCalled);
    }

    [Fact]
    public void Ask_Behavior_Shows_The_Close_Prompt()
    {
        (SearchViewModel vm, FakeWindowService window) = Register();
        vm.SaveSettings(s => s with { CloseBehavior = CloseBehavior.Ask });
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();

        window.RaiseCloseRequested();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".modal-box")));
    }

    [Fact]
    public void MinimizeToTray_Behavior_Hides_To_Tray_Without_Showing_The_Prompt()
    {
        (SearchViewModel vm, FakeWindowService window) = Register();
        vm.SaveSettings(s => s with { CloseBehavior = CloseBehavior.MinimizeToTray });
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();

        window.RaiseCloseRequested();

        Assert.True(window.HideToTrayCalled);
        Assert.Empty(cut.FindAll(".modal-box"));
    }
}
