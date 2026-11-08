using System.Reflection;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class PatternEditorTests : BunitContext
{
    private readonly FakeFileDialog _dialog = new();

    public PatternEditorTests()
    {
        Services.AddSingleton<Core.Services.IFileDialogService>(_dialog);
    }

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
    public void The_Fourth_Mode_Shows_Two_Boxes_And_Adds_A_Paired_Chip()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Pair");
        Assert.Single(cut.FindAll("input.pattern"));
        Assert.Single(cut.FindAll("input.pattern-ends"));

        cut.Find("input.pattern").Input("abc");
        cut.Find("input.pattern-ends").Input("xyz");
        cut.Find("input.pattern-ends").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.Contains("abc", cut.Find(".chip").TextContent);
        Assert.Contains("xyz", cut.Find(".chip").TextContent);
    }

    [Fact]
    public void The_Fourth_Mode_Also_Commits_From_The_First_Box()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Pair");
        cut.Find("input.pattern").Input("abc");
        cut.Find("input.pattern-ends").Input("xyz");
        cut.Find("input.pattern").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
    }

    [Fact]
    public void Only_One_Box_Is_Shown_Outside_The_Fourth_Mode()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        Assert.Empty(cut.FindAll("input.pattern-ends"));

        cut.Find("select.pos").Change("Pair");
        Assert.Single(cut.FindAll("input.pattern-ends"));

        cut.Find("select.pos").Change("Contains");
        Assert.Empty(cut.FindAll("input.pattern-ends"));
    }

    [Fact]
    public void A_Paired_Chip_Warns_About_The_Half_That_Cannot_Occur()
    {
        SearchViewModel vm = Vm();
        vm.AddPair("abc", "@", ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("cannot occur", cut.Find(".chip-warn").TextContent);
    }

    [Fact]
    public void A_Paired_Chip_Labels_Both_Halves()
    {
        SearchViewModel vm = Vm();
        vm.AddPair("abc", "xyz", ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        string chip = cut.Find(".chip").TextContent;
        Assert.Contains("starts", chip);
        Assert.Contains("abc", chip);
        Assert.Contains("ends", chip);
        Assert.Contains("xyz", chip);
        Assert.DoesNotContain("pair", chip);
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
    public void Typing_Into_The_Box_Updates_The_View_Models_Entry_Text()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.pattern").Input("abc");

        Assert.Equal("abc", vm.EntryText);
    }

    [Fact]
    public void The_Hint_Line_Names_The_Typed_Text_When_The_Entry_Is_Reachable()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.pattern").Input("ab");

        cut.WaitForAssertion(() => Assert.Equal("Press Enter to add \"ab\". Start adds it too.", cut.Find(".pattern-hint").TextContent.Trim()));
        Assert.Empty(cut.FindAll(".pattern-problem"));
    }

    [Fact]
    public void The_Problem_Line_Names_The_Typed_Text_For_An_Unreachable_Entry()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        // "@" is not in the XRPL base58 alphabet and stays unreachable after normalization.
        cut.Find("input.pattern").Input("@");

        cut.WaitForAssertion(() => Assert.Equal("\"@\" cannot occur in an XRPL address.", cut.Find(".pattern-problem").TextContent.Trim()));
        Assert.Empty(cut.FindAll(".pattern-hint"));
    }

    [Fact]
    public void A_Chip_Whose_Text_Is_Folded_Shows_The_Normalized_Form()
    {
        SearchViewModel vm = Vm();
        // "Il" folds to "iL" (I -> i, l -> L): reachable, but not what the user typed.
        vm.AddPattern("Il", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("→ iL", cut.Find(".chip-normalized").TextContent.Trim());
    }

    [Fact]
    public void A_Chip_That_Needs_No_Folding_Shows_No_Normalized_Marker()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Empty(cut.FindAll(".chip-normalized"));
    }

    [Fact]
    public void The_Hint_Names_The_Normalized_Form_When_Typing_Folds_The_Entry()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.pattern").Input("Il");

        cut.WaitForAssertion(() => Assert.Equal(
            "\"Il\" is searched as \"iL\". Press Enter to add it, or just press Start.",
            cut.Find(".pattern-hint").TextContent.Trim()));
    }

    [Fact]
    public void The_Hint_Keeps_The_Plain_Wording_When_Typing_Needs_No_Folding()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.pattern").Input("ab");

        cut.WaitForAssertion(() => Assert.Equal(
            "Press Enter to add \"ab\". Start adds it too.",
            cut.Find(".pattern-hint").TextContent.Trim()));
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

    [Fact]
    public void Save_Writes_Every_Chip_In_The_Full_Format()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        _dialog.PathToReturn = path;
        try
        {
            SearchViewModel vm = Vm();
            vm.AddPair("static", "bit", ignoreCase: true);
            vm.AddPattern("RRRRRRRR", MatchPosition.Ends, ignoreCase: true);
            vm.AddPattern("ALTCONN", MatchPosition.Starts, ignoreCase: false);
            IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

            cut.Find("button.save-list").Click();

            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));
            Assert.Equal(
                "starts\tstatic\tends\tbit\nends\tRRRRRRRR\nstarts\tALTCONN\tAa\n",
                File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void What_Save_Writes_Loads_Back_As_The_Same_Chips()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        _dialog.PathToReturn = path;
        try
        {
            SearchViewModel saved = Vm();
            saved.AddPair("static", "bit", ignoreCase: false);
            saved.AddPattern("RRRRRRRR", MatchPosition.Ends, ignoreCase: true);
            saved.AddPattern("fast", MatchPosition.Contains, ignoreCase: false);
            IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, saved));
            cut.Find("button.save-list").Click();
            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));

            SearchViewModel loaded = Vm();
            // The entry checkbox says the opposite of every saved chip, so a per-chip flag that
            // failed to survive the file would come back wrong rather than coincidentally right.
            Assert.Equal(new ListLoadResult(3, 0, 0), loaded.AddPatternsFromList(File.ReadAllLines(path), ignoreCase: true));

            Assert.Equal(
                saved.Patterns.Select(c => (c.Text, c.EndsText, c.Position, c.IgnoreCase)),
                loaded.Patterns.Select(c => (c.Text, c.EndsText, c.Position, c.IgnoreCase)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_Is_Disabled_Until_There_Is_A_Chip_To_Save()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.True(cut.Find("button.save-list").HasAttribute("disabled"));

        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save-list").HasAttribute("disabled")));
    }

    [Fact]
    public void A_Cancelled_Save_Writes_Nothing_And_Shows_No_Error()
    {
        _dialog.PathToReturn = null;
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("button.save-list").Click();

        Assert.Empty(cut.FindAll(".list-error"));
    }

    [Fact]
    public void A_Save_Failure_Is_Shown_Instead_Of_Thrown()
    {
        // A directory that does not exist: the write throws DirectoryNotFoundException (an IOException).
        _dialog.PathToReturn = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "patterns.txt");
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("button.save-list").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not save the list", cut.Find(".list-error").TextContent));
    }

    [Fact]
    public async Task Lines_The_List_Grammar_Rejects_Are_Reported()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        InputFileChangeEventArgs args = new(new[] { new StringBrowserFile("ends\tbit\nsideways\tstatic\nstarts\ta\tends\n") });
        MethodInfo onListChosen = typeof(PatternEditor).GetMethod("OnListChosen", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await cut.InvokeAsync(() => (Task)onListChosen.Invoke(cut.Instance, new object[] { args })!);
        cut.Render();

        Assert.Contains("2 lines could not be read", cut.Find(".list-note").TextContent);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("bit", chip.Text);
    }

    [Fact]
    public async Task Lines_Naming_A_Rule_Already_In_The_Set_Are_Reported_As_Duplicates()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("bit", MatchPosition.Ends, ignoreCase: true);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        await LoadList(cut, "ends\tbit\nstarts\tstatic\nends\tbit\n");

        Assert.Contains("2 duplicates skipped", cut.Find(".list-note").TextContent);
        Assert.Equal(2, vm.Patterns.Count);
    }

    [Fact]
    public void Entering_A_Rule_The_Set_Already_Holds_Says_So_Instead_Of_Doubling_It()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("fast", MatchPosition.Contains, ignoreCase: true);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Contains");
        cut.Find("input.pattern").Input("FAST");
        cut.Find("input.pattern").KeyDown("Enter");

        Assert.Contains("already in the list", cut.Find(".list-note").TextContent);
        Assert.Single(vm.Patterns);
    }

    [Fact]
    public void The_Card_Title_Counts_The_Chips()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        vm.AddPattern("cd", MatchPosition.Ends, ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        Assert.Equal("2", cut.Find(".card-title .count").TextContent.Trim());
    }

    [Fact]
    public void Clear_Is_Disabled_Without_Chips_And_Only_Asks_The_Host_To_Clear()
    {
        SearchViewModel vm = Vm();
        int asked = 0;
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p
            .Add(x => x.Vm, vm)
            .Add(x => x.OnClearRequested, () => asked++));
        Assert.True(cut.Find("button.clear-patterns").HasAttribute("disabled"));

        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.clear-patterns").HasAttribute("disabled")));
        cut.Find("button.clear-patterns").Click();

        // The editor never clears the set itself: the page owns the confirmation.
        Assert.Equal(1, asked);
        Assert.Single(vm.Patterns);
    }

    [Fact]
    public void A_Short_Set_Shows_Every_Chip_And_Offers_No_Toggle()
    {
        SearchViewModel vm = Vm();
        AddChips(vm, 12);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        Assert.Equal(12, cut.FindAll(".chip").Count);
        Assert.Empty(cut.FindAll("button.chips-toggle"));
    }

    [Fact]
    public void A_Long_Set_Shows_The_First_Ten_And_Counts_The_Rest()
    {
        SearchViewModel vm = Vm();
        AddChips(vm, 40);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        Assert.Equal(10, cut.FindAll(".chip").Count);
        Assert.Equal("+30 more", cut.Find("button.chips-toggle").TextContent.Trim());
    }

    [Fact]
    public void Expanding_Shows_Every_Chip_And_Offers_To_Collapse_Again()
    {
        SearchViewModel vm = Vm();
        AddChips(vm, 40);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("button.chips-toggle").Click();

        Assert.Equal(40, cut.FindAll(".chip").Count);
        Assert.Equal("show less", cut.Find("button.chips-toggle").TextContent.Trim());
    }

    [Fact]
    public void A_Set_That_Shrinks_Below_The_Threshold_Loses_The_Toggle()
    {
        SearchViewModel vm = Vm();
        AddChips(vm, 40);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        cut.Find("button.chips-toggle").Click();

        vm.ClearPatterns();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button.chips-toggle")));
        Assert.Empty(cut.FindAll(".chip"));
    }

    private static void AddChips(SearchViewModel vm, int count)
    {
        for (int i = 0; i < count; i++)
        {
            vm.AddPattern($"pattern{i}", MatchPosition.Starts, ignoreCase: true);
        }
    }

    private static async Task LoadList(IRenderedComponent<PatternEditor> cut, string content)
    {
        InputFileChangeEventArgs args = new(new[] { new StringBrowserFile(content) });
        MethodInfo onListChosen = typeof(PatternEditor).GetMethod("OnListChosen", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)onListChosen.Invoke(cut.Instance, new object[] { args })!);
        cut.Render();
    }

    private sealed class StringBrowserFile : IBrowserFile
    {
        private readonly byte[] _content;

        public StringBrowserFile(string content) => _content = Encoding.UTF8.GetBytes(content);

        public string Name => "patterns.txt";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => _content.Length;
        public string ContentType => "text/plain";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(_content);
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
