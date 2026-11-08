using System.Diagnostics;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Layout;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        // The tutorial bubble loads its script module once the offer appears.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private (SearchViewModel Vm, FakeWindowService Window) Register()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        FakeWindowService window = new();
        Services.AddSingleton(vm);
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
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

    [Fact]
    public void Offers_The_Tutorial_To_A_New_User_And_Keeps_It_Through_A_Route_Change()
    {
        (SearchViewModel vm, FakeWindowService _) = Register();
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();

        cut.WaitForAssertion(() => Assert.Equal("Take a short tutorial?", cut.Find(".tutorial-bubble h3").TextContent));

        cut.Find("button.tutorial-start").Click();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/settings");

        cut.WaitForAssertion(() => Assert.Equal("Add a word", cut.Find(".tutorial-bubble h3").TextContent));
        Assert.True(vm.IsSandbox);
    }

    [Theory]
    [InlineData(CloseBehavior.Ask)]
    [InlineData(CloseBehavior.MinimizeToTray)]
    [InlineData(CloseBehavior.Exit)]
    public void Closing_The_Window_Leaves_The_Tutorial_First(CloseBehavior behavior)
    {
        (SearchViewModel vm, FakeWindowService window) = Register();
        // Already offered, so the layout's own offer check cannot race the Begin below.
        vm.SaveSettings(s => s with { CloseBehavior = behavior, TutorialOffered = true });
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();
        Assert.True(tutorial.Begin());
        Assert.True(vm.IsSandbox);

        window.RaiseCloseRequested();

        Assert.False(tutorial.IsActive);
        Assert.False(vm.IsSandbox);
        Assert.Equal(behavior == CloseBehavior.MinimizeToTray, window.HideToTrayCalled);
        Assert.Equal(behavior == CloseBehavior.Exit, window.ExitCalled);
        if (behavior == CloseBehavior.Ask)
        {
            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".modal-box")));
        }
    }

    [Fact]
    public void Closing_The_Window_During_A_Tutorial_Search_Stops_It_And_Gives_The_Users_State_Back()
    {
        (SearchViewModel vm, FakeWindowService window) = Register();
        vm.SaveSettings(s => s with { CloseBehavior = CloseBehavior.Exit, TutorialOffered = true });
        Assert.True(vm.AddPattern("user", XrplVanity.Core.MatchPosition.Starts, false));
        vm.Count = 7;
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        IRenderedComponent<MainLayout> cut = Render<MainLayout>();
        Assert.True(tutorial.Begin());
        Assert.True(vm.AddPattern("zzzzzzzz", XrplVanity.Core.MatchPosition.Starts, false));
        vm.Start();
        Stopwatch sw = Stopwatch.StartNew();
        while (vm.State != SearchState.Searching && sw.Elapsed < TimeSpan.FromSeconds(60)) Thread.Sleep(25);
        Assert.Equal(SearchState.Searching, vm.State);

        window.RaiseCloseRequested();

        Assert.False(tutorial.IsActive);
        Assert.True(window.ExitCalled);
        sw.Restart();
        while ((vm.IsSandbox || vm.State != SearchState.Idle) && sw.Elapsed < TimeSpan.FromSeconds(60)) Thread.Sleep(25);
        Assert.False(vm.IsSandbox);
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Equal("user", Assert.Single(vm.Patterns).Text);
        Assert.Equal(7, vm.Count);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Does_Not_Offer_The_Tutorial_Twice()
    {
        (SearchViewModel vm, FakeWindowService _) = Register();
        vm.SaveSettings(s => s with { TutorialOffered = true });

        IRenderedComponent<MainLayout> cut = Render<MainLayout>();
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);   // the offer check runs off the UI thread

        Assert.Empty(cut.FindAll(".tutorial-bubble"));
    }
}
