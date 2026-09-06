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
    public void Start_Is_Disabled_With_A_Reason_Until_A_Pattern_Exists()
    {
        SearchViewModel vm = Register();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(cut.Find("button.start").HasAttribute("disabled"));
        Assert.Contains("Add at least one reachable pattern.", cut.Find(".start-hint").TextContent);

        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
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
}
