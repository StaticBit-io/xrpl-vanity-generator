using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchPageTests : BunitContext
{
    private readonly MemorySettingsStore _settings = new();

    private SearchViewModel Register()
    {
        SearchViewModel vm = new(_settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton(new TutorialService(vm, new MemoryResultStore()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        return vm;
    }

    [Fact]
    public void Switching_Language_Keeps_State_And_Translates_Messages()
    {
        SearchViewModel vm = Register();
        TextCatalog english = TextCatalog.For(UiLanguage.English);
        IRenderedComponent<CatalogHost> host = Render<CatalogHost>(p => p
            .Add(h => h.Catalog, english)
            .Add(h => h.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<SearchPage>(0);
                b.CloseComponent();
            })));
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        vm.OnMatch(new XrplVanity.Core.Match("rReal", "sReal", "ab", XrplVanity.Core.MatchPosition.Starts), sandboxSession: false);
        vm.FlushPendingMatches();
        // "0" is folded onto "o", so with a chip present the message that shows is the "searched as" hint.
        vm.EntryText = "0";
        host.WaitForAssertion(() =>
        {
            Assert.Equal("ab", host.Find(".chip .mono").TextContent);
            Assert.Equal("rReal", host.Find(".result-row .address").TextContent);
            Assert.Equal("\"0\" is searched as \"o\". Press Enter to add it, or just press Start.", host.Find(".pattern-hint").TextContent);
        });

        host.InvokeAsync(() => host.Instance.Swap(TextCatalog.Transformed(UiLanguage.Russian, phrase => "[ru] " + phrase)));

        host.WaitForAssertion(() =>
        {
            Assert.StartsWith("[ru] ", host.Find(".pattern-hint").TextContent, StringComparison.Ordinal);
            Assert.Equal("ab", host.Find(".chip .mono").TextContent);
            Assert.Equal("rReal", host.Find(".result-row .address").TextContent);
            Assert.Equal("0", host.Find("input.pattern").GetAttribute("value"));
            Assert.StartsWith("[ru] ", host.Find("button.start").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>Cascades a catalog that the test can replace, the way the shell does when the language changes.</summary>
    private sealed class CatalogHost : ComponentBase
    {
        [Parameter] public TextCatalog Catalog { get; set; } = TextCatalog.For(UiLanguage.English);

        [Parameter] public RenderFragment? ChildContent { get; set; }

        public void Swap(TextCatalog catalog)
        {
            Catalog = catalog;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TextCatalog>>(0);
            builder.AddAttribute(1, "Value", Catalog);
            builder.AddAttribute(2, "ChildContent", ChildContent);
            builder.CloseComponent();
        }
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

    [Fact]
    public void Every_Card_Sits_In_A_Region()
    {
        Register();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();

        string RegionOf(string selector) => cut.Find(selector).Closest("[data-tutorial]")!.GetAttribute("data-tutorial")!;

        Assert.Equal(TutorialRegions.Title, RegionOf("h1"));
        Assert.Equal(TutorialRegions.Start, RegionOf("button.start"));
        Assert.Equal(TutorialRegions.Patterns, RegionOf(".chips"));
        Assert.Equal(TutorialRegions.Estimate, RegionOf(".estimate"));
        Assert.Equal(TutorialRegions.Devices, RegionOf("button.measure"));
        Assert.Equal(TutorialRegions.Duty, RegionOf(".duty-value"));
        Assert.Equal(TutorialRegions.Stats, RegionOf(".tiles"));
        Assert.Equal(TutorialRegions.Results, RegionOf(".results"));
    }

    [Fact]
    public void Opening_And_Closing_The_Qr_Dialog_Moves_The_Tutorial_On()
    {
        SearchViewModel vm = Register();
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        tutorial.Next();
        Assert.Equal(TutorialStep.QrButton, tutorial.Current!.Step);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-qr")));

        cut.Find("button.show-qr").Click();

        Assert.Equal(TutorialStep.QrDialog, tutorial.Current!.Step);
        cut.Find("button.close").Click();
        Assert.True(tutorial.Current!.Step > TutorialStep.QrDialog);
    }

    [Fact]
    public void Qr_Dialog_Carries_The_Tutorial_Text_On_That_Step()
    {
        SearchViewModel vm = Register();
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        tutorial.Next();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-qr")));

        cut.Find("button.show-qr").Click();

        string body = TextCatalog.For(UiLanguage.English).Render(TutorialScript.Steps.Single(s => s.Step == TutorialStep.QrDialog).Body);
        Assert.Equal(body, cut.Find(".qr-box .tutorial-note").TextContent);
    }

    [Fact]
    public void Qr_Dialog_Has_No_Tutorial_Text_Outside_The_Tutorial()
    {
        SearchViewModel vm = Register();
        vm.OnMatch(new XrplVanity.Core.Match("rReal", "sReal", "ab", XrplVanity.Core.MatchPosition.Starts), sandboxSession: false);
        vm.FlushPendingMatches();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-qr")));

        cut.Find("button.show-qr").Click();

        Assert.NotEmpty(cut.FindAll(".qr-box"));
        Assert.Empty(cut.FindAll(".tutorial-note"));
    }

    [Fact]
    public void The_Tutorial_Result_Cannot_Leave_Through_Reveal_Copy_Or_Export()
    {
        SearchViewModel vm = Register();
        TutorialFixtures.ReachResult(vm, Services.GetRequiredService<TutorialService>());
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();

        Assert.True(cut.Find("button.reveal").HasAttribute("disabled"));
        Assert.True(cut.Find("button.copy-address").HasAttribute("disabled"));
        Assert.True(cut.Find("button.copy-seed").HasAttribute("disabled"));
        Assert.True(cut.Find("button.export").HasAttribute("disabled"));
        Assert.False(cut.Find("button.show-qr").HasAttribute("disabled"));
    }

    [Fact]
    public void Leaving_The_Tutorial_Closes_An_Open_Clear_Dialog_And_Keeps_The_Users_Words()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("user1", XrplVanity.Core.MatchPosition.Starts, true);
        vm.AddPattern("user2", XrplVanity.Core.MatchPosition.Starts, true);
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(tutorial.Begin());
        vm.AddPattern("abcdefhjkm", XrplVanity.Core.MatchPosition.Starts, true);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.clear-patterns").HasAttribute("disabled")));
        cut.Find("button.clear-patterns").Click();
        Assert.NotEmpty(cut.FindAll(".modal-box"));

        tutorial.Leave();

        // The dialog sits above the bubble, so Skip can be pressed behind it; it must not outlive the
        // tutorial and offer to wipe words that are not the practice ones.
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal-box")));
        Assert.Equal(2, vm.Patterns.Count);
        Assert.Equal(2, _settings.Current.Patterns.Count);
    }

    [Fact]
    public void A_Step_Change_Closes_An_Open_Clear_Dialog()
    {
        SearchViewModel vm = Register();
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(tutorial.Begin());
        vm.AddPattern("abcdefhjkm", XrplVanity.Core.MatchPosition.Starts, true);
        Assert.Equal(TutorialStep.ListFiles, tutorial.Current!.Step);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.clear-patterns").HasAttribute("disabled")));
        cut.Find("button.clear-patterns").Click();
        Assert.True(tutorial.PageDialogOpen);

        tutorial.Next();

        Assert.Equal(TutorialStep.Devices, tutorial.Current!.Step);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal-box")));
        Assert.False(tutorial.PageDialogOpen);
        Assert.Single(vm.Patterns);
    }

    [Fact]
    public void The_Clear_Dialog_Tells_The_Tutorial_While_It_Is_Open()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.False(tutorial.PageDialogOpen);

        cut.Find("button.clear-patterns").Click();
        Assert.True(tutorial.PageDialogOpen);

        cut.Find("button.cancel").Click();
        Assert.False(tutorial.PageDialogOpen);

        cut.Find("button.clear-patterns").Click();
        cut.Instance.Dispose();
        Assert.False(tutorial.PageDialogOpen);
    }

    [Fact]
    public void The_Qr_Dialog_Of_The_Practice_Result_Goes_With_The_Tutorial()
    {
        SearchViewModel vm = Register();
        TutorialService tutorial = Services.GetRequiredService<TutorialService>();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        tutorial.Next();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-qr")));
        cut.Find("button.show-qr").Click();
        Assert.Equal(TutorialStep.QrDialog, tutorial.Current!.Step);

        // A window close with minimise to tray leaves the tutorial from outside the page.
        tutorial.Leave();

        Assert.False(vm.IsSandbox);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".qr-box")), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_Qr_Dialog_Of_A_Real_Result_Stays_When_The_Tutorial_Is_Not_Involved()
    {
        SearchViewModel vm = Register();
        vm.OnMatch(new XrplVanity.Core.Match("rReal", "sReal", "ab", XrplVanity.Core.MatchPosition.Starts), sandboxSession: false);
        vm.FlushPendingMatches();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-qr")));
        cut.Find("button.show-qr").Click();

        vm.AddPattern("cd", XrplVanity.Core.MatchPosition.Starts, false);
        Services.GetRequiredService<TutorialService>().Leave();

        Assert.NotEmpty(cut.FindAll(".qr-box"));
    }
}
