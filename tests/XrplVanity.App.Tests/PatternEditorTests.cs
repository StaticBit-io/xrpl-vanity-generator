using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class PatternEditorTests : BunitContext
{
    private static SearchViewModel Vm() => new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());

    [Fact]
    public void Enter_Adds_A_Chip_With_The_Selected_Position_And_Case()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Contains");
        cut.Find("input.case").Change(false);
        cut.Find("input.pattern").Input("fast");
        cut.Find("input.pattern").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("fast", chip.Text);
        Assert.Equal(XrplVanity.Core.MatchPosition.Contains, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Contains("fast", cut.Find(".chip").TextContent);
        Assert.Equal(string.Empty, cut.Find("input.pattern").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void No_Reachable_Pattern_Shows_The_Problem_Under_The_Entry_Row()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("Add at least one reachable pattern.", cut.Find(".pattern-problem").TextContent.Trim());

        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".pattern-problem")));
    }

    [Fact]
    public void An_Unreachable_Chip_Shows_Its_Warning()
    {
        SearchViewModel vm = Vm();
        // "@" is not in the XRPL base58 alphabet and stays unreachable after normalization
        // (unlike "0", which the normalizer folds onto "o" and which would then be reachable).
        vm.AddPattern("@", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("cannot occur in an XRPL address", cut.Find(".chip-warn").TextContent.Trim());
    }

    [Fact]
    public void The_Remove_Button_Drops_The_Chip()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        cut.Find(".chip button.remove").Click();
        Assert.Empty(vm.Patterns);
        Assert.Empty(cut.FindAll(".chip"));
    }

    [Fact]
    public void Disposing_The_Component_Stops_It_From_Reacting_To_Later_View_Model_Changes()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Instance.Dispose();

        // Vm.Changed is raised from search worker threads (see SearchViewModel); after Dispose the
        // component must ignore it instead of throwing against a torn-down render tree.
        Exception? caught = null;
        Thread worker = new(() =>
        {
            try
            {
                vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        worker.Start();
        worker.Join();

        Assert.Null(caught);
    }

    [Fact]
    public async Task A_List_Read_Failure_Is_Shown_Instead_Of_Thrown()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        InputFileChangeEventArgs args = new(new[] { new ThrowingBrowserFile() });
        MethodInfo onListChosen = typeof(PatternEditor).GetMethod("OnListChosen", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await cut.InvokeAsync(() => (Task)onListChosen.Invoke(cut.Instance, new object[] { args })!);
        cut.Render();

        Assert.Contains("Could not read the list", cut.Find(".list-error").TextContent);
        Assert.Empty(vm.Patterns);
    }

    private sealed class ThrowingBrowserFile : IBrowserFile
    {
        public string Name => "big-list.txt";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => 2 * 1024 * 1024;
        public string ContentType => "text/plain";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            throw new IOException("The file exceeds the allowed size.");
    }
}
