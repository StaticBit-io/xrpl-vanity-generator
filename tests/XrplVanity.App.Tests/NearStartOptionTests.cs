using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class NearStartOptionTests : BunitContext
{
    public NearStartOptionTests()
    {
        Services.AddSingleton<Core.Services.IFileDialogService>(new FakeFileDialog());
    }

    private static SearchViewModel Vm(MemorySettingsStore? settings = null) =>
        new(settings ?? new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());

    [Fact]
    public void The_Option_Is_Off_By_Default_And_A_Starts_Entry_Stays_Starts()
    {
        SearchViewModel vm = Vm();
        vm.EntryText = "XRPLFILE";

        vm.AddPatternFromEntry();

        Assert.False(vm.EntryNearStart);
        Assert.Equal(MatchPosition.Starts, Assert.Single(vm.Patterns).Position);
    }

    [Fact]
    public void With_The_Option_On_A_Starts_Entry_Commits_As_NearStart()
    {
        SearchViewModel vm = Vm();
        vm.EntryNearStart = true;
        vm.EntryText = "XRPLFILE";

        vm.AddPatternFromEntry();

        Assert.Equal(MatchPosition.NearStart, Assert.Single(vm.Patterns).Position);
    }

    [Fact]
    public void The_Option_Leaves_Every_Other_Mode_Alone()
    {
        SearchViewModel vm = Vm();
        vm.EntryNearStart = true;
        vm.EntryPosition = MatchPosition.Contains;
        vm.EntryText = "XRPLFILE";

        vm.AddPatternFromEntry();

        Assert.Equal(MatchPosition.Contains, Assert.Single(vm.Patterns).Position);
    }

    [Theory]
    [InlineData("XRPLFILE", false)]
    [InlineData("STATICBIT", false)]
    [InlineData("xrplfile", true)]   // both 'x' and 'X' are unreachable after the r
    public void The_Entry_Hint_Offers_The_Option_When_The_First_Character_Is_Rare(string text, bool ignoreCase)
    {
        SearchViewModel vm = Vm();
        vm.EntryIgnoreCase = ignoreCase;
        vm.EntryText = text;

        Assert.NotNull(vm.NearStartHint);
        Assert.Equal(SearchText.NearStartHint, vm.NearStartHint.Id);
        Assert.Equal(text, vm.NearStartHint.Arg("pattern"));
        Assert.Equal(24, Math.Round((double)vm.NearStartHint.Arg("speedup")!));
    }

    [Theory]
    [InlineData("pshn", false)]      // 'p' can follow the r
    [InlineData("static", true)]     // 's' can, so 's'/'S' together gain under 2x
    [InlineData("", false)]
    [InlineData("X#", false)]        // unreachable: no estimate to improve
    public void The_Entry_Hint_Stays_Quiet_When_The_Gain_Is_Small_Or_Undefined(string text, bool ignoreCase)
    {
        SearchViewModel vm = Vm();
        vm.EntryIgnoreCase = ignoreCase;
        vm.EntryText = text;

        Assert.Null(vm.NearStartHint);
    }

    [Fact]
    public void The_Entry_Hint_Goes_Away_Once_The_Option_Is_On_Or_The_Mode_Changes()
    {
        SearchViewModel vm = Vm();
        vm.EntryIgnoreCase = false;
        vm.EntryText = "XRPLFILE";
        Assert.NotNull(vm.NearStartHint);

        vm.EntryNearStart = true;
        Assert.Null(vm.NearStartHint);

        vm.EntryNearStart = false;
        vm.EntryPosition = MatchPosition.Ends;
        Assert.Null(vm.NearStartHint);
    }

    [Fact]
    public void A_Rare_Starts_Chip_Carries_Its_Speedup_And_A_Common_One_Does_Not()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("XRPLFILE", MatchPosition.Starts, ignoreCase: false);
        vm.AddPattern("pshn", MatchPosition.Starts, ignoreCase: false);
        vm.AddPattern("XRPLFILE", MatchPosition.Ends, ignoreCase: false);

        Assert.InRange(vm.Patterns[0].NearStartSpeedup!.Value, 24.0, 24.7);
        Assert.Null(vm.Patterns[1].NearStartSpeedup);
        Assert.Null(vm.Patterns[2].NearStartSpeedup);
    }

    [Fact]
    public void Taking_The_Chip_Offer_Makes_It_NearStart_Cuts_The_Estimate_And_Persists()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = Vm(settings);
        vm.AddPattern("XRPLFILE", MatchPosition.Starts, ignoreCase: false);
        double before = vm.ExpectedCandidates!.Value;

        vm.AllowNearStart(vm.Patterns[0]);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal(MatchPosition.NearStart, chip.Position);
        Assert.Null(chip.NearStartSpeedup);
        Assert.InRange(before / vm.ExpectedCandidates!.Value, 24.0, 24.7);
        Assert.Equal("NearStart", Assert.Single(settings.Current.Patterns).Position);
    }

    [Fact]
    public void Taking_The_Offer_When_The_NearStart_Twin_Exists_Drops_The_Chip()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("XRPLFILE", MatchPosition.NearStart, ignoreCase: false);
        vm.AddPattern("XRPLFILE", MatchPosition.Starts, ignoreCase: false);

        vm.AllowNearStart(vm.Patterns[1]);

        Assert.Equal(MatchPosition.NearStart, Assert.Single(vm.Patterns).Position);
    }

    [Fact]
    public void A_Saved_NearStart_Chip_Is_Restored()
    {
        MemorySettingsStore settings = new();
        settings.Save(new AppSettings { Patterns = new[] { new PatternDraft("XRPLFILE", "NearStart", false) } });

        SearchViewModel vm = Vm(settings);

        Assert.Equal(MatchPosition.NearStart, Assert.Single(vm.Patterns).Position);
    }

    [Fact]
    public void A_NearStart_Chip_Round_Trips_Through_A_Pattern_List()
    {
        string line = PatternListFormat.Format(new PatternChip("XRPLFILE", MatchPosition.NearStart, ignoreCase: false));

        Assert.Equal("nearstart\tXRPLFILE\tAa", line);
        Assert.True(PatternListFormat.TryParse(line, defaultIgnoreCase: true, out PatternChip chip));
        Assert.Equal(MatchPosition.NearStart, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Equal("XRPLFILE", chip.Text);
    }

    [Fact]
    public void The_Editor_Shows_The_Box_Only_In_Starts_Mode_And_Commits_A_NearStart_Chip()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.near-start").Change(true);
        cut.Find("input.pattern").Input("XRPLFILE");
        cut.Find("input.pattern").KeyDown("Enter");

        Assert.Equal(MatchPosition.NearStart, Assert.Single(vm.Patterns).Position);
        Assert.Contains("2nd–3rd", cut.Find(".chip .chip-near").TextContent);

        cut.Find("select.pos").Change("Contains");
        Assert.Empty(cut.FindAll("input.near-start"));
    }

    [Fact]
    public void The_Editor_Hints_Below_The_Entry_And_Offers_On_The_Chip()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("input.case").Change(false);
        cut.Find("input.pattern").Input("XRPLFILE");
        Assert.Contains("about 24× sooner", cut.Find(".near-start-hint").TextContent);

        cut.Find("input.pattern").KeyDown("Enter");
        Assert.Empty(cut.FindAll(".near-start-hint"));
        Assert.Contains("24× sooner", cut.Find(".chip-speedup").TextContent);

        cut.Find(".chip-speedup").Click();

        Assert.Equal(MatchPosition.NearStart, Assert.Single(vm.Patterns).Position);
        Assert.Empty(cut.FindAll(".chip-speedup"));
    }

    [Fact]
    public void With_The_Option_On_A_Pair_Entry_Commits_A_NearStart_Pair()
    {
        SearchViewModel vm = Vm();
        vm.EntryNearStart = true;
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "XRPL";
        vm.EntryEndsText = "FILE";

        vm.AddPatternFromEntry();

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.True(chip.NearStart);
        PatternSet nearPair = new PatternSet.Builder().AddPair("XRPL", "FILE", chip.IgnoreCase, nearStart: true).Build();
        Assert.Equal(SearchEstimator.ExpectedCandidatesPerMatch(nearPair), vm.ExpectedCandidates);
    }

    [Fact]
    public void The_Entry_Hint_Offers_The_Option_For_The_Starts_Half_Of_A_Pair()
    {
        SearchViewModel vm = Vm();
        vm.EntryIgnoreCase = false;
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "XRPL";
        vm.EntryEndsText = "FILE";

        Assert.Equal(24, Math.Round((double)vm.NearStartHint!.Arg("speedup")!));

        vm.EntryText = "pshn";
        Assert.Null(vm.NearStartHint);
    }

    [Fact]
    public void A_Pair_And_Its_NearStart_Twin_Are_Two_Rules()
    {
        SearchViewModel vm = Vm();

        Assert.True(vm.AddPair("XRPL", "FILE", ignoreCase: false));
        Assert.True(vm.AddPair("XRPL", "FILE", ignoreCase: false, nearStart: true));
        Assert.False(vm.AddPair("XRPL", "FILE", ignoreCase: false, nearStart: true));
    }

    [Fact]
    public void Taking_The_Offer_On_A_Pair_Chip_Makes_It_NearStart_Cuts_The_Estimate_And_Persists()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = Vm(settings);
        vm.AddPair("XRPL", "FILE", ignoreCase: false);
        PatternChip chip = vm.Patterns[0];
        Assert.InRange(chip.NearStartSpeedup!.Value, 24.0, 24.7);
        double before = vm.ExpectedCandidates!.Value;

        vm.AllowNearStart(chip);

        Assert.True(chip.NearStart);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.Null(chip.NearStartSpeedup);
        Assert.InRange(before / vm.ExpectedCandidates!.Value, 24.0, 24.7);
        PatternDraft draft = Assert.Single(settings.Current.Patterns);
        Assert.True(draft.NearStart);

        SearchViewModel restored = Vm(settings);
        Assert.True(Assert.Single(restored.Patterns).NearStart);
    }

    [Fact]
    public void Taking_The_Offer_On_A_Pair_Whose_Twin_Exists_Drops_The_Chip()
    {
        SearchViewModel vm = Vm();
        vm.AddPair("XRPL", "FILE", ignoreCase: false, nearStart: true);
        vm.AddPair("XRPL", "FILE", ignoreCase: false);

        vm.AllowNearStart(vm.Patterns[1]);

        Assert.True(Assert.Single(vm.Patterns).NearStart);
    }

    [Fact]
    public void A_NearStart_Pair_Round_Trips_Through_A_Pattern_List()
    {
        PatternChip near = new("XRPL", MatchPosition.Pair, ignoreCase: false, "FILE") { NearStart = true };
        string line = PatternListFormat.Format(near);

        Assert.Equal("nearstart\tXRPL\tends\tFILE\tAa", line);
        Assert.True(PatternListFormat.TryParse(line, defaultIgnoreCase: true, out PatternChip chip));
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.True(chip.NearStart);
        Assert.Equal("FILE", chip.EndsText);
        Assert.Equal("starts\tXRPL\tends\tFILE\tAa",
            PatternListFormat.Format(new PatternChip("XRPL", MatchPosition.Pair, ignoreCase: false, "FILE")));
    }

    [Fact]
    public void The_Editor_Shows_The_Box_In_Pair_Mode_And_Marks_The_NearStart_Pair_Chip()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Pair");
        cut.Find("input.near-start").Change(true);
        cut.Find("input.pattern").Input("XRPL");
        cut.Find("input.pattern-ends").Input("FILE");
        cut.Find("input.pattern").KeyDown("Enter");

        Assert.True(Assert.Single(vm.Patterns).NearStart);
        Assert.Contains("2nd–3rd", cut.Find(".chip .chip-near").TextContent);
        Assert.Empty(cut.FindAll(".chip-speedup"));
    }
}
