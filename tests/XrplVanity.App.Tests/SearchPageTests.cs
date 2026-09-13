using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchPageTests : BunitContext
{
    private SearchViewModel Register()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        return vm;
    }

    [Fact]
    public void Start_Is_Disabled_Until_A_Pattern_Exists_And_The_Reason_Is_In_The_Patterns_Card_Not_Beside_Start()
    {
        SearchViewModel vm = Register();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(cut.Find("button.start").HasAttribute("disabled"));
        Assert.Contains("Add at least one reachable pattern.", cut.Find(".pattern-problem").TextContent);
        Assert.Empty(cut.FindAll(".start-hint"));

        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.start").HasAttribute("disabled")));
    }

    [Fact]
    public void Start_Is_Enabled_With_Only_Pending_Text_And_No_Hint_Appears_Beside_It()
    {
        SearchViewModel vm = Register();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();

        vm.EntryText = "ab";

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.start").HasAttribute("disabled")));
        Assert.Empty(cut.FindAll(".start-hint"));   // the target hint is unaffected by the pending entry
    }

    [Fact]
    public void Start_Is_Disabled_With_A_Reason_Beside_Start_Until_A_Target_Is_Selected()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        vm.SetTargetSelected("cpu", false);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(cut.Find("button.start").HasAttribute("disabled"));
        Assert.Contains("Select the CPU or at least one device.", cut.Find(".start-hint").TextContent);
        Assert.Empty(cut.FindAll(".pattern-problem"));

        vm.SetTargetSelected("cpu", true);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.start").HasAttribute("disabled")));
    }

    [Fact]
    public void The_Header_Shows_The_Status_Pill_And_Swaps_Start_For_Stop_While_Searching()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("zzzzzzzz", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        cut.Find("button.start").Click();
        cut.WaitForAssertion(() => Assert.Equal("Searching", cut.Find(".pill").TextContent.Trim()), TimeSpan.FromSeconds(30));
        cut.Find("button.stop").Click();
        cut.WaitForAssertion(() => Assert.Equal("Idle", cut.Find(".pill").TextContent.Trim()), TimeSpan.FromSeconds(30));
    }
    [Fact]
    public void Clearing_The_Patterns_Asks_First_And_Cancelling_Keeps_Them()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();

        cut.Find("button.clear-patterns").Click();

        Assert.Contains("Clear 1 pattern?", cut.Find(".modal-box h3").TextContent);
        // The page behind the dialog is inert, the way a History delete leaves it.
        Assert.True(cut.Find("header.page-head").HasAttribute("inert"));
        Assert.True(cut.Find(".content-scroll").HasAttribute("inert"));

        cut.Find("button.cancel").Click();

        Assert.Single(vm.Patterns);
        Assert.Empty(cut.FindAll(".modal-box"));
        Assert.False(cut.Find("header.page-head").HasAttribute("inert"));
    }

    [Fact]
    public void Confirming_The_Clear_Empties_The_Patterns()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        vm.AddPattern("cd", XrplVanity.Core.MatchPosition.Ends, false);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();

        cut.Find("button.clear-patterns").Click();
        Assert.Contains("Clear 2 patterns?", cut.Find(".modal-box h3").TextContent);
        cut.Find("button.confirm").Click();

        Assert.Empty(vm.Patterns);
        Assert.Empty(cut.FindAll(".modal-box"));
    }
}
