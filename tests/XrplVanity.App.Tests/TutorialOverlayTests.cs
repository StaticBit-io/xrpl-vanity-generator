using System.Diagnostics;
using Bunit;
using Bunit.Extensions.WaitForHelpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class TutorialOverlayTests : BunitContext
{
    private const string ModulePath = "./_content/XrplVanity.App.Core/js/tutorial.js";
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    public TutorialOverlayTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private (SearchViewModel Vm, TutorialService Tutorial) Register()
    {
        MemoryResultStore store = new();
        SearchViewModel vm = new(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        TutorialService tutorial = new(vm, store);
        Services.AddSingleton(vm);
        Services.AddSingleton(tutorial);
        return (vm, tutorial);
    }

    private static void ReachQrDialog(SearchViewModel vm, TutorialService tutorial)
    {
        TutorialFixtures.ReachResult(vm, tutorial);
        tutorial.Next();
        tutorial.QrOpened();
        Assert.Equal(TutorialStep.QrDialog, tutorial.Current!.Step);
    }

    /// <summary>Behaves like the bUnit runtime except that importing the script module fails, as it does when the file is missing.</summary>
    private sealed class ImportFailingRuntime(IJSRuntime inner) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            identifier == "import"
                ? ValueTask.FromException<TValue>(new JSException("module missing"))
                : inner.InvokeAsync<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "import"
                ? ValueTask.FromException<TValue>(new JSException("module missing"))
                : inner.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    private int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier == FocusIdentifier);

    [Fact]
    public void Shows_Nothing_When_Inactive()
    {
        Register();

        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        Assert.Empty(cut.FindAll(".tutorial-bubble"));
    }

    [Fact]
    public void Shows_Title_Body_Step_Number_And_Skip()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());

        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        AngleSharp.Dom.IElement bubble = cut.Find(".tutorial-bubble");
        Assert.Equal("dialog", bubble.GetAttribute("role"));
        Assert.Equal("-1", bubble.GetAttribute("tabindex"));
        Assert.Equal("Add a word", cut.Find("h3").TextContent);
        Assert.Contains("ten letters or more", cut.Find("p").TextContent);
        Assert.Equal($"Step 1 of {tutorial.StepCount}", cut.Find(".tutorial-step").TextContent.Trim());
        Assert.Equal("Skip", cut.Find("button.tutorial-skip").TextContent.Trim());

        cut.Find("button.tutorial-skip").Click();

        Assert.False(tutorial.IsActive);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-bubble")));
    }

    [Fact]
    public void Next_Appears_Only_When_The_Step_Allows_It()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        Assert.Empty(cut.FindAll("button.tutorial-next"));

        Assert.True(vm.AddPattern("abcdefhjkm", MatchPosition.Starts, true));
        cut.WaitForAssertion(() => Assert.Equal("Next", cut.Find("button.tutorial-next").TextContent.Trim()));

        cut.Find("button.tutorial-next").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(TutorialStep.Devices, tutorial.Current!.Step);
            Assert.Empty(cut.FindAll("button.tutorial-next"));
        });
    }

    [Fact]
    public void Last_Step_Offers_Finish_Which_Leaves()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);
        tutorial.Next();
        tutorial.QrOpened();
        tutorial.QrClosed();
        tutorial.Next();
        tutorial.Next();
        tutorial.Next();
        Assert.Equal(TutorialStep.Done, tutorial.Current!.Step);
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        Assert.Equal("Finish", cut.Find("button.tutorial-next").TextContent.Trim());
        Assert.Empty(cut.FindAll("button.tutorial-skip"));
        cut.Find("button.tutorial-next").Click();

        Assert.False(tutorial.IsActive);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-bubble")));
    }

    [Fact]
    public void Offer_Shows_Start_And_Not_Now()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        tutorial.ShowOffer();
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        Assert.Equal("Take a short tutorial?", cut.Find("h3").TextContent);
        Assert.Empty(cut.FindAll(".tutorial-step"));
        Assert.Empty(cut.FindAll("button.tutorial-skip"));
        Assert.Equal("Start tutorial", cut.Find("button.tutorial-start").TextContent.Trim());
        Assert.Equal("Not now", cut.Find("button.tutorial-decline").TextContent.Trim());

        cut.Find("button.tutorial-decline").Click();

        Assert.False(tutorial.IsActive);
        Assert.True(vm.Settings.TutorialOffered);
        Assert.False(vm.IsSandbox);
    }

    [Fact]
    public void Offer_Start_Begins_The_Practice_Run()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        tutorial.ShowOffer();
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        cut.Find("button.tutorial-start").Click();

        Assert.Equal(TutorialStep.AddWord, tutorial.Current!.Step);
        Assert.True(vm.IsSandbox);
        cut.WaitForAssertion(() => Assert.Equal("Add a word", cut.Find("h3").TextContent));
    }

    [Fact]
    public void Navigates_To_The_Route_Of_The_Step()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.False(nav.Uri.EndsWith("/settings", StringComparison.Ordinal));

        tutorial.Next();
        tutorial.QrOpened();
        tutorial.QrClosed();   // no GPU in this catalog, so the load step is left out
        Assert.Equal(TutorialStep.StopAfter, tutorial.Current!.Step);

        cut.WaitForAssertion(() => Assert.EndsWith("/settings", nav.Uri, StringComparison.Ordinal));

        tutorial.Next();
        tutorial.Next();
        tutorial.Next();   // Done goes back to the search page
        cut.WaitForAssertion(() => Assert.False(nav.Uri.EndsWith("/settings", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Escape_Asks_Before_Leaving()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        Assert.Empty(cut.FindAll(".modal-box"));

        await cut.InvokeAsync(() => cut.Instance.OnEscape());

        Assert.Equal("Leave the tutorial?", cut.Find(".modal-box h3").TextContent);
        Assert.Equal("Leave", cut.Find(".modal-box button.confirm").TextContent.Trim());
        Assert.True(tutorial.IsActive);

        cut.Find(".modal-box button.cancel").Click();

        Assert.Empty(cut.FindAll(".modal-box"));
        Assert.True(tutorial.IsActive);

        await cut.InvokeAsync(() => cut.Instance.OnEscape());
        cut.Find(".modal-box button.confirm").Click();

        Assert.False(tutorial.IsActive);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-bubble")));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal-box")));
    }

    [Fact]
    public async Task Escape_On_The_Qr_Dialog_Step_Is_Left_To_The_Dialog()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        ReachQrDialog(vm, tutorial);
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        await cut.InvokeAsync(() => cut.Instance.OnEscape());

        Assert.Empty(cut.FindAll(".modal-box"));
        Assert.True(tutorial.IsActive);
        Assert.Equal(TutorialStep.QrDialog, tutorial.Current!.Step);
    }

    [Fact]
    public void No_Bubble_On_The_Qr_Dialog_Step()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        tutorial.Next();
        cut.WaitForAssertion(() => Assert.Equal("Move it to a wallet", cut.Find("h3").TextContent));

        tutorial.QrOpened();

        Assert.Equal(TutorialStep.QrDialog, tutorial.Current!.Step);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-bubble")));

        tutorial.QrClosed();   // no GPU in this catalog, so the next step is on the Settings page

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tutorial-bubble")));
    }

    [Fact]
    public void Focus_Moves_To_The_Bubble_On_A_New_Step()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        cut.WaitForAssertion(() => Assert.Equal(1, FocusCalls()));
        ElementReference focused = Assert.IsType<ElementReference>(JSInterop.Invocations.Single(i => i.Identifier == FocusIdentifier).Arguments[0]);
        Assert.False(string.IsNullOrEmpty(focused.Id));

        Assert.True(vm.AddPattern("abcdefhjkm", MatchPosition.Starts, true));   // moves to ListFiles

        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls()));
    }

    [Fact]
    public void Focus_Is_Left_To_The_Qr_Dialog_On_Its_Step()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        cut.WaitForAssertion(() => Assert.Equal(1, FocusCalls()));

        tutorial.Next();                // QrButton: the bubble takes focus again
        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls()));
        tutorial.QrOpened();            // QrDialog: the dialog focuses its own Close button

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-bubble")));
        Assert.Equal(2, FocusCalls());
    }

    [Fact]
    public void Places_The_Bubble_Beside_The_Measured_Target()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        BunitJSModuleInterop module = JSInterop.SetupModule(ModulePath);
        module.Setup<TutorialMeasurement>("measure", _ => true)
            .SetResult(new TutorialMeasurement(new[] { new TutorialRect(100, 100, 200, 100) }, 1100, 720, 320, 200));
        Assert.True(tutorial.Begin());

        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        cut.WaitForAssertion(() =>
        {
            string style = cut.Find(".tutorial-bubble").GetAttribute("style")!;
            Assert.Contains("left:312px", style);
            Assert.Contains("top:100px", style);
        });
    }

    [Fact]
    public async Task A_View_Change_Moves_The_Bubble_And_Does_Not_Re_Reveal()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        BunitJSModuleInterop module = JSInterop.SetupModule(ModulePath);
        module.Setup<TutorialMeasurement>("measure", _ => true)
            .SetResult(new TutorialMeasurement(Array.Empty<TutorialRect>(), 1100, 720, 320, 200));
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        cut.WaitForAssertion(() => Assert.Contains("left:390px", cut.Find(".tutorial-bubble").GetAttribute("style")!));

        module.Setup<TutorialMeasurement>("measure", _ => true)
            .SetResult(new TutorialMeasurement(new[] { new TutorialRect(100, 100, 200, 100) }, 1100, 720, 320, 200));
        await cut.InvokeAsync(() => cut.Instance.OnViewChanged());

        cut.WaitForAssertion(() => Assert.Contains("left:312px", cut.Find(".tutorial-bubble").GetAttribute("style")!));
        Assert.Contains(module.Invocations, i => i.Identifier == "measure" && Equals(i.Arguments[1], false));
    }

    [Fact]
    public void The_Bubble_Goes_Inert_And_Behind_While_A_Page_Dialog_Is_Open()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        Assert.False(cut.Find(".tutorial-bubble").HasAttribute("inert"));
        Assert.DoesNotContain("behind", cut.Find(".tutorial-bubble").GetAttribute("class"));

        tutorial.SetPageDialogOpen(true);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find(".tutorial-bubble").HasAttribute("inert"));
            Assert.Contains("behind", cut.Find(".tutorial-bubble").GetAttribute("class"));
        });

        tutorial.SetPageDialogOpen(false);

        cut.WaitForAssertion(() =>
        {
            Assert.False(cut.Find(".tutorial-bubble").HasAttribute("inert"));
            Assert.DoesNotContain("behind", cut.Find(".tutorial-bubble").GetAttribute("class"));
        });
    }

    [Fact]
    public void A_Status_Shows_While_The_Practice_Run_Is_Still_Winding_Down_After_Leave()
    {
        // The measurement stops within milliseconds of the Leave, so the text is on screen only
        // briefly: try a few runs until one catches it. Each run gets a context of its own because a
        // service cannot be registered after the first render.
        bool seen = false;
        for (int attempt = 0; attempt < 25 && !seen; attempt++)
        {
            using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            MemoryResultStore store = new();
            SearchViewModel vm = new(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
            TutorialService tutorial = new(vm, store);
            context.Services.AddSingleton(vm);
            context.Services.AddSingleton(tutorial);
            Assert.True(tutorial.Begin());
            IRenderedComponent<TutorialOverlay> cut = context.Render<TutorialOverlay>();
            vm.MeasureSpeed();

            tutorial.Leave();

            try
            {
                cut.WaitForAssertion(() =>
                {
                    Assert.Equal("Leaving the tutorial...", cut.Find(".tutorial-status").TextContent.Trim());
                    Assert.Equal("status", cut.Find(".tutorial-status").GetAttribute("role"));
                    Assert.Empty(cut.FindAll(".tutorial-bubble"));
                }, TimeSpan.FromMilliseconds(300));
                seen = true;
            }
            catch (WaitForFailedException)
            {
                // The run ended before the render; try again.
            }

            Stopwatch sw = Stopwatch.StartNew();
            while (vm.IsSandbox && sw.Elapsed < TimeSpan.FromSeconds(60)) Thread.Sleep(10);
            Assert.False(vm.IsSandbox);
            if (seen)
            {
                cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tutorial-status")));
            }
        }

        Assert.True(seen, "no run showed the status while the sandbox was still on");
    }

    [Fact]
    public void The_Bubble_Still_Shows_When_The_Script_Module_Cannot_Be_Loaded()
    {
        Services.AddSingleton<IJSRuntime>(new ImportFailingRuntime(JSInterop.JSRuntime));
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);
        tutorial.Next();
        tutorial.QrOpened();
        tutorial.QrClosed();   // no GPU in this catalog, so the next step is on the Settings page
        Assert.Equal(TutorialStep.StopAfter, tutorial.Current!.Step);

        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();

        // Without the module nothing measures the page, so the stylesheet's default has to show it.
        cut.WaitForAssertion(() =>
        {
            string bubbleClass = cut.Find(".tutorial-bubble").GetAttribute("class")!;
            Assert.DoesNotContain("unplaced", bubbleClass);
        });
    }

    [Fact]
    public async Task Disposing_Stops_Watching_And_Ignores_Late_Notifications()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        BunitJSModuleInterop module = JSInterop.SetupModule(ModulePath);
        Assert.True(tutorial.Begin());
        IRenderedComponent<TutorialOverlay> cut = Render<TutorialOverlay>();
        cut.WaitForAssertion(() => Assert.Contains(module.Invocations, i => i.Identifier == "watch"));

        await cut.Instance.DisposeAsync();
        tutorial.Leave();   // Changed fires into a torn-down component

        Assert.Contains(module.Invocations, i => i.Identifier == "unwatch");
    }
}
