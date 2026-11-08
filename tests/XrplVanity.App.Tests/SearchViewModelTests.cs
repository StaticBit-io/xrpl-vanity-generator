using System.Diagnostics;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchViewModelTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    private static (SearchViewModel Vm, FakeNotifications Notes, MemoryResultStore Results, MemorySettingsStore Settings) Create(params DeviceCatalogEntry[] devices)
    {
        FakeNotifications notes = new();
        MemoryResultStore results = new();
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, results, notes, () => devices);
        return (vm, notes, results, settings);
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < Deadline) Thread.Sleep(25);
        Assert.True(condition(), $"timed out waiting for {what}");
    }

    [Fact]
    public void Starts_Idle_With_The_Cpu_Target_And_No_Patterns()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Empty(vm.Patterns);
        Assert.False(vm.CanStart);
        Assert.Equal(UiText.Of(SearchText.AddAtLeastOnePattern), vm.StartBlockedReason);
        TargetChoice cpu = Assert.Single(vm.Targets);
        Assert.Equal("cpu", cpu.Id);
        Assert.True(cpu.Selected);
    }

    [Fact]
    public void Adding_A_Chip_Rebuilds_The_Pattern_Set_And_The_Estimate()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);

        Assert.True(vm.CanStart);

        PatternSet.Builder builder = new();
        builder.Add(MatchPosition.Starts, "ab", false);
        PatternSet set = builder.Build();
        double? expected = SearchEstimator.ExpectedCandidatesPerMatch(set);

        Assert.Equal(expected, vm.ExpectedCandidates);
        Assert.InRange(vm.ExpectedCandidates!.Value, 1300, 1400);
        Assert.Null(vm.EstimatedTime);   // no rate yet
        Assert.Equal(UiText.Of(SearchText.RateUnknown), vm.EstimateNote);
    }

    [Fact]
    public void An_Unreachable_Chip_Carries_A_Warning_And_Blocks_Start()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("@", MatchPosition.Starts, ignoreCase: false);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("cannot occur in an XRPL address", chip.Warning);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public void A_Chip_Whose_Text_Is_Folded_Records_The_Normalized_Form()
    {
        // "Il" folds to "iL" (I -> i, l -> L): reachable, but not what the user typed.
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("Il", MatchPosition.Starts, false);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("iL", chip.Normalized);
    }

    [Fact]
    public void A_Chip_That_Needs_No_Folding_Has_No_Normalized_Form()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, false);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Null(chip.Normalized);
    }

    [Fact]
    public void PatternHint_Names_The_Normalized_Form_When_The_Entry_Folds()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryText = "Il";
        Assert.Equal(UiText.Of(SearchText.PatternSearchedAs, ("typed", "Il"), ("normalized", "iL")), vm.PatternHint);
    }

    [Fact]
    public void Deselecting_Every_Target_Blocks_Start()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.SetTargetSelected("cpu", false);
        Assert.False(vm.CanStart);
        Assert.Equal(UiText.Of(SearchText.SelectTarget), vm.StartBlockedReason);
    }

    [Fact]
    public void Cpu_Devices_From_The_Catalog_Are_Listed_But_Unselected()
    {
        (SearchViewModel vm, _, _, _) = Create(
            new DeviceCatalogEntry(new DeviceSelector("cuda", 0), "RTX", "sm_120", false),
            new DeviceCatalogEntry(new DeviceSelector("opencl", 1), "pocl", "cpu", true));
        Assert.Equal(new[] { "cpu", "cuda:0", "opencl:1" }, vm.Targets.Select(t => t.Id));
        Assert.False(vm.Targets[2].Selected);
        Assert.True(vm.Targets[2].IsCpuDevice);
        Assert.False(vm.NoGpuFound);
    }

    [Fact]
    public void No_Devices_Sets_The_NoGpu_Banner_Flag()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.True(vm.NoGpuFound);
    }

    [Fact]
    public void A_Cpu_Search_Runs_To_The_Count_Persists_Notifies_And_Stores_A_Rate_Hint()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, MemorySettingsStore settings) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.CpuThreads = 2;
        vm.Count = 3;
        int stateChanges = 0;
        vm.Changed += () => Interlocked.Increment(ref stateChanges);

        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        // OnCompleted goes Idle first and notifies last, so this is the end of the completion work.
        WaitUntil(() => { lock (notes.Sent) return notes.Sent.Any(n => n.Title.Id == NotificationText.SearchFinishedTitle); }, "the finish notification");
        Assert.Equal(SessionOutcome.CountReached, vm.LastSummary!.Outcome);
        Assert.Equal(3, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.StartsWith("ra", r.Address));
        Assert.Equal(3, results.LoadAll().Count);
        Assert.Equal(0, results.Appends);   // batched through AppendRange; never one write per hit
        Assert.Contains(notes.Sent, n => (n.Title.Id == NotificationText.MatchFoundTitle || n.Title.Id == NotificationText.MatchesFoundTitle) && !n.Body.ToString().Contains(vm.Results[0].Seed));
        Assert.Contains(notes.Sent, n => n.Title.Id == NotificationText.SearchFinishedTitle && Equals(n.Body.Arg("count"), 3L) && Equals(n.Body.Arg("outcome"), NotificationText.OutcomeCountReached));
        Assert.True(settings.Current.RateHints.ContainsKey("cpu"), "rate hint stored");
        Assert.True(stateChanges > 0);
        Assert.NotNull(vm.EstimatedTime);   // now computed from the hint
    }

    [Fact]
    public void Matches_Are_Persisted_In_Batches_With_Fewer_Notifications_Than_Hits()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.CpuThreads = 2;
        vm.Count = 50;

        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Equal(SessionOutcome.CountReached, vm.LastSummary!.Outcome);
        Assert.Equal(50, vm.Results.Count);
        Assert.Equal(50, results.LoadAll().Count);
        Assert.Equal(0, results.Appends);            // OnMatch itself writes nothing
        Assert.True(results.AppendRanges <= 50, "one batch write per flush, not per hit");

        int matchNotifications = notes.Sent.Count(n => n.Title.Id == NotificationText.MatchFoundTitle || n.Title.Id == NotificationText.MatchesFoundTitle);
        Assert.True(matchNotifications < 50, $"expected fewer notifications than matches, got {matchNotifications}");
    }

    [Fact]
    public void A_Failing_Result_Store_Surfaces_A_History_Banner_Without_Breaking_The_Search()
    {
        FakeNotifications notes = new();
        MemoryResultStore results = new() { ThrowOnWrite = true, ReadOnlyReason = UiText.Of(HistoryText.ResultsCouldNotBeWritten, ("detail", "disk full")) };
        SearchViewModel vm = new(new MemorySettingsStore(), results, notes, () => Array.Empty<DeviceCatalogEntry>());
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;

        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Single(vm.Results);
        Assert.Equal(UiText.Of(HistoryText.ResultsCouldNotBeWritten, ("detail", "disk full")), vm.HistoryReadOnlyReason);
    }

    [Fact]
    public void A_Settings_Store_That_Throws_Does_Not_Prevent_A_Session_From_Completing()
    {
        MemorySettingsStore settings = new() { ThrowOnSave = true };
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;

        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Single(vm.Results);
    }

    [Fact]
    public void Stop_Returns_To_Idle_With_Cancelled()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
        Assert.Equal(SessionOutcome.Cancelled, vm.LastSummary!.Outcome);
    }

    [Fact]
    public void Duty_Changes_During_A_Search_Are_Forwarded_And_Saved()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");
        vm.DutyPercent = 50;
        Assert.Equal(50, settings.Current.DutyPercent);
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
    }

    [Fact]
    public void Read_Only_Store_Does_Not_Break_A_Search()
    {
        FakeNotifications notes = new();
        MemoryResultStore results = new() { IsReadOnly = true, ReadOnlyReason = UiText.Of(HistoryText.ResultsCouldNotBeRead, ("detail", "corrupt")) };
        SearchViewModel vm = new(new MemorySettingsStore(), results, notes, () => Array.Empty<DeviceCatalogEntry>());
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Single(vm.Results);
        Assert.Equal(UiText.Of(HistoryText.ResultsCouldNotBeRead, ("detail", "corrupt")), vm.HistoryReadOnlyReason);
    }

    [Fact]
    public void Setting_CpuThreads_To_Zero_Clamps_To_One_And_Persists()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        int changes = 0;
        vm.Changed += () => Interlocked.Increment(ref changes);

        vm.CpuThreads = 0;

        Assert.Equal(1, vm.CpuThreads);
        Assert.Equal(1, settings.Current.CpuThreads);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Setting_Count_To_Zero_Clamps_To_Unbounded_And_Persists()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.Count = 5;   // a non-null baseline so the assignment below is an observable change
        int changes = 0;
        vm.Changed += () => Interlocked.Increment(ref changes);

        vm.Count = 0;

        Assert.Null(vm.Count);
        Assert.Null(settings.Current.Count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_Corrupt_Saved_Duty_Percent_Is_Clamped_On_Construction()
    {
        MemorySettingsStore settings = new();
        settings.Save(settings.Current with { DutyPercent = 500 });
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());

        Assert.Equal(100, vm.DutyPercent);
    }

    [Fact]
    public void A_Faulted_Session_Completion_Returns_To_Idle_With_The_Failure_Message()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        vm.OnSessionCompleted(Task.FromException<SearchSummary>(new InvalidOperationException("boom")));

        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Equal(UiText.Of(SearchText.SearchFailed, ("detail", "boom")), vm.LastFailure);

        vm.Dispose();   // in case the real (stopped) session had not settled yet
    }

    [Fact]
    public void A_Gpu_Failure_Keeps_The_Driver_Text_Inside_A_Translated_Sentence()
    {
        // An OpenCL device that exists on no machine, so the measurement reports a driver failure.
        DeviceCatalogEntry ghost = new(new DeviceSelector("opencl", 99), "Ghost", "none", false);
        (SearchViewModel vm, _, _, _) = Create(ghost);
        vm.SetTargetSelected("opencl:99", true);
        vm.SetTargetSelected("cpu", false);

        vm.MeasureSpeed();

        WaitUntil(() => !vm.IsCalibrating && vm.CalibrationFailure is not null, "the measurement to report its failure");
        UiText failure = vm.CalibrationFailure!;
        Assert.Equal(SearchText.CalibrationFailed, failure.Id);
        string detail = Assert.IsType<string>(failure.Arg("detail"));
        Assert.NotEmpty(detail);
        string english = TextCatalog.For(UiLanguage.English).Render(failure);
        Assert.EndsWith(detail, english, StringComparison.Ordinal);
        Assert.NotEqual(detail, english);
    }

    [Fact]
    public void A_Match_Notification_Carries_Address_Pattern_And_Position_As_Values()
    {
        (SearchViewModel vm, FakeNotifications notes, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;

        vm.Start();

        WaitUntil(() => { lock (notes.Sent) return notes.Sent.Any(n => n.Title.Id == NotificationText.SearchFinishedTitle); }, "the finish notification");
        (UiText Title, UiText Body) match;
        lock (notes.Sent) match = notes.Sent.First(n => n.Title.Id == NotificationText.MatchFoundTitle);
        SessionResult found = Assert.Single(vm.Results);
        Assert.Equal(
            UiText.Of(NotificationText.MatchFoundBody, ("address", found.Address), ("pattern", found.Pattern), ("position", PatternsText.Position(found.Position))),
            match.Body);
    }

    [Fact]
    public void A_Cancelled_Session_Completion_Returns_To_Idle_With_A_Cancelled_Message()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        TaskCompletionSource<SearchSummary> tcs = new();
        tcs.SetCanceled(TestContext.Current.CancellationToken);
        vm.OnSessionCompleted(tcs.Task);

        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Equal(UiText.Of(SearchText.SearchCancelled), vm.LastFailure);

        vm.Dispose();
    }

    [Fact]
    public void Results_Returns_The_Same_Instance_Until_A_Match_Changes_It()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;

        IReadOnlyList<SessionResult> before = vm.Results;
        Assert.Same(before, vm.Results);   // no match yet: repeated reads return the same instance

        vm.Start();
        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");

        IReadOnlyList<SessionResult> after = vm.Results;
        Assert.NotSame(before, after);     // OnMatch rebuilt the snapshot
        Assert.Single(after);
        Assert.Same(after, vm.Results);    // stable again until the next change
    }

    [Fact]
    public void Setting_EntryText_Raises_Changed()
    {
        (SearchViewModel vm, _, _, _) = Create();
        int changes = 0;
        vm.Changed += () => Interlocked.Increment(ref changes);

        vm.EntryText = "ab";

        Assert.Equal("ab", vm.EntryText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddPatternFromEntry_Adds_The_Chip_And_Clears_The_Entry()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryText = "ab";
        vm.EntryPosition = MatchPosition.Contains;
        vm.EntryIgnoreCase = false;

        bool added = vm.AddPatternFromEntry();

        Assert.True(added);
        Assert.Equal(string.Empty, vm.EntryText);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("ab", chip.Text);
        Assert.Equal(MatchPosition.Contains, chip.Position);
        Assert.False(chip.IgnoreCase);
    }

    [Fact]
    public void AddPatternFromEntry_On_A_Blank_Entry_Adds_Nothing_And_Returns_False()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.False(vm.AddPatternFromEntry());
        Assert.Empty(vm.Patterns);
    }

    [Fact]
    public void Start_With_Only_Pending_Text_Commits_It_And_The_Search_Runs()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryText = "a";
        vm.Count = 1;

        Assert.True(vm.CanStart);
        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("a", chip.Text);
        Assert.Single(vm.Results);
    }

    [Fact]
    public void The_Same_Rule_Added_Twice_Stays_One_Chip()
    {
        (SearchViewModel vm, _, _, _) = Create();

        Assert.True(vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false));
        Assert.False(vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false));

        Assert.Single(vm.Patterns);
    }

    [Fact]
    public void Two_Spellings_Of_One_Base58_Look_Alike_Are_The_Same_Rule()
    {
        (SearchViewModel vm, _, _, _) = Create();

        // The normalizer folds 0 onto o, so both chips would search for exactly the same thing.
        Assert.True(vm.AddPattern("00000000", MatchPosition.Starts, ignoreCase: true));
        Assert.False(vm.AddPattern("oooooooo", MatchPosition.Starts, ignoreCase: true));

        Assert.Single(vm.Patterns);
    }

    [Fact]
    public void Case_Alone_Separates_Two_Rules_Only_When_The_Case_Matters()
    {
        (SearchViewModel vm, _, _, _) = Create();

        Assert.True(vm.AddPattern("static", MatchPosition.Starts, ignoreCase: true));
        Assert.False(vm.AddPattern("STATIC", MatchPosition.Starts, ignoreCase: true));

        // Case-sensitively the two are different searches, and both are also narrower than the
        // case-insensitive rule already there.
        Assert.True(vm.AddPattern("static", MatchPosition.Starts, ignoreCase: false));
        Assert.True(vm.AddPattern("STATIC", MatchPosition.Starts, ignoreCase: false));

        Assert.Equal(3, vm.Patterns.Count);
    }

    [Fact]
    public void A_Pair_Is_A_Duplicate_Only_When_Both_Halves_Match()
    {
        (SearchViewModel vm, _, _, _) = Create();

        Assert.True(vm.AddPair("abc", "xyz", ignoreCase: false));
        Assert.False(vm.AddPair("abc", "xyz", ignoreCase: false));
        Assert.True(vm.AddPair("abc", "zzz", ignoreCase: false));

        Assert.Equal(2, vm.Patterns.Count);
    }

    [Fact]
    public void The_Same_Word_At_Two_Positions_Stays_Two_Rules()
    {
        (SearchViewModel vm, _, _, _) = Create();

        Assert.True(vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false));
        Assert.True(vm.AddPattern("ab", MatchPosition.Ends, ignoreCase: false));

        Assert.Equal(2, vm.Patterns.Count);
    }

    [Fact]
    public void Loading_The_Same_List_Twice_Adds_Nothing_The_Second_Time()
    {
        (SearchViewModel vm, _, _, _) = Create();
        string[] list = { "starts\tstatic\tends\tbit", "ends\tRRRRRRRR", "contains\tfast\tAa" };

        ListLoadResult first = vm.AddPatternsFromList(list, ignoreCase: true);
        ListLoadResult second = vm.AddPatternsFromList(list, ignoreCase: true);

        Assert.Equal(new ListLoadResult(3, 0, 0), first);
        Assert.Equal(new ListLoadResult(0, 3, 0), second);
        Assert.Equal(3, vm.Patterns.Count);
    }

    [Fact]
    public void A_List_That_Repeats_Itself_Loads_Each_Rule_Once()
    {
        (SearchViewModel vm, _, _, _) = Create();

        ListLoadResult result = vm.AddPatternsFromList(
            new[] { "static", "static", "sideways\tx", "ledger" },
            ignoreCase: true);

        Assert.Equal(new ListLoadResult(2, 1, 1), result);
        Assert.Equal(2, vm.Patterns.Count);
    }

    [Fact]
    public void ClearPatterns_Empties_The_Set_And_Blocks_Start_Again()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        Assert.True(vm.CanStart);

        vm.ClearPatterns();

        Assert.Empty(vm.Patterns);
        Assert.False(vm.CanStart);
        Assert.Equal(UiText.Of(SearchText.AddAtLeastOnePattern), vm.StartBlockedReason);
        Assert.Empty(settings.Current.Patterns);
    }

    [Fact]
    public void A_List_In_The_Full_Format_Restores_Positions_Pairs_And_Case()
    {
        (SearchViewModel vm, _, _, _) = Create();

        ListLoadResult result = vm.AddPatternsFromList(
            new[] { "starts\tstatic\tends\tbit", "ends\tRRRRRRRR", "contains\tALTCONN\tAa", "" },
            ignoreCase: true);

        Assert.Equal(new ListLoadResult(3, 0, 0), result);
        Assert.Collection(
            vm.Patterns,
            c => { Assert.Equal("static", c.Text); Assert.Equal("bit", c.EndsText); Assert.Equal(MatchPosition.Pair, c.Position); Assert.True(c.IgnoreCase); },
            c => { Assert.Equal("RRRRRRRR", c.Text); Assert.Equal(MatchPosition.Ends, c.Position); Assert.True(c.IgnoreCase); },
            c => { Assert.Equal("ALTCONN", c.Text); Assert.Equal(MatchPosition.Contains, c.Position); Assert.False(c.IgnoreCase); });
    }

    [Fact]
    public void A_Plain_Word_List_Still_Loads_As_Starts_Chips_With_The_Entry_Case()
    {
        (SearchViewModel vm, _, _, _) = Create();

        ListLoadResult result = vm.AddPatternsFromList(new[] { "static", "  ledger  ", "" }, ignoreCase: false);

        Assert.Equal(new ListLoadResult(2, 0, 0), result);
        Assert.Collection(
            vm.Patterns,
            c => { Assert.Equal("static", c.Text); Assert.Equal(MatchPosition.Starts, c.Position); Assert.False(c.IgnoreCase); },
            c => { Assert.Equal("ledger", c.Text); Assert.Equal(MatchPosition.Starts, c.Position); Assert.False(c.IgnoreCase); });
    }

    [Fact]
    public void Lines_Outside_The_Grammar_Are_Skipped_And_Counted()
    {
        (SearchViewModel vm, _, _, _) = Create();

        ListLoadResult result = vm.AddPatternsFromList(new[] { "sideways\tstatic", "ends\tbit", "starts\ta\tends" }, ignoreCase: true);

        Assert.Equal(new ListLoadResult(1, 0, 2), result);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("bit", chip.Text);
    }

    [Fact]
    public void AddPair_Adds_A_Paired_Chip_And_Rebuilds_The_Set()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("abc", "xyz", ignoreCase: false);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Null(chip.Warning);

        PatternSet.Builder builder = new();
        builder.AddPair("abc", "xyz", false);
        Assert.Equal(SearchEstimator.ExpectedCandidatesPerMatch(builder.Build()), vm.ExpectedCandidates);
    }

    [Fact]
    public void ExpectedCandidates_For_A_Paired_Rule_Counts_Both_Halves()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("abc", "xyz", ignoreCase: false);
        Assert.InRange(vm.ExpectedCandidates!.Value, 1.5e10, 1.6e10);
    }

    [Fact]
    public void A_Paired_Rule_Alone_Makes_CanStart_True()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("ab", "cd", ignoreCase: false);
        Assert.Equal(MatchPosition.Pair, Assert.Single(vm.Patterns).Position);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public void A_Paired_Chip_Warns_About_The_First_Half_That_Cannot_Occur()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("abc", "@", ignoreCase: false);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("cannot occur in an XRPL address", chip.Warning);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public void A_Paired_Chip_Reports_The_Case_Fold_Cap_Of_Either_Half()
    {
        // The cap is applied per half by PatternSet.Builder, so a long second half must be
        // reported even when the first half is short enough to fold.
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("ab", "cdefghijkmnpqr", ignoreCase: true);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal($"longer than {PatternSet.MaxIgnoreCaseLength} characters: searched case-sensitively", chip.Warning);
    }

    [Fact]
    public void A_Paired_Chip_Whose_Halves_Fold_Records_Both_Normalized_Halves()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPair("Il", "ab", ignoreCase: false);
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("iL..ab", chip.Normalized);
    }

    [Fact]
    public void Flipping_A_Plain_Chip_To_Pair_Warns_Instead_Of_Throwing()
    {
        // UpdatePattern is public and can set any chip's Position to Pair; the chip then has no
        // second half, contributes no rule, and must say so rather than dereference a null.
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);
        PatternChip chip = Assert.Single(vm.Patterns);

        vm.UpdatePattern(chip, MatchPosition.Pair, ignoreCase: false);

        Assert.Equal("empty pattern", chip.Warning);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public void EntryEndsText_Round_Trips_And_Raises_Changed_Once_Per_Distinct_Value()
    {
        (SearchViewModel vm, _, _, _) = Create();
        int changes = 0;
        vm.Changed += () => Interlocked.Increment(ref changes);

        vm.EntryEndsText = "xyz";
        vm.EntryEndsText = "xyz";

        Assert.Equal("xyz", vm.EntryEndsText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_Pending_Pair_Is_Reachable_Only_When_Both_Halves_Are()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "abc";

        Assert.Equal(UiText.Of(SearchText.AddAtLeastOnePattern), vm.PatternProblem);
        Assert.Null(vm.PatternHint);
        Assert.False(vm.CanStart);

        vm.EntryEndsText = "@";
        Assert.Equal(UiText.Of(SearchText.PatternUnreachable, ("pattern", "@")), vm.PatternProblem);
        Assert.Null(vm.PatternHint);

        vm.EntryEndsText = "xyz";
        Assert.Null(vm.PatternProblem);
        Assert.Equal(UiText.Of(SearchText.PatternPressEnter, ("typed", "abc..xyz")), vm.PatternHint);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public void The_Pending_Pair_Hint_Previews_Both_Normalized_Halves()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "Il";
        vm.EntryEndsText = "ab";

        Assert.Equal(UiText.Of(SearchText.PatternSearchedAs, ("typed", "Il..ab"), ("normalized", "iL..ab")), vm.PatternHint);
    }

    [Fact]
    public void AddPatternFromEntry_Commits_Both_Boxes_As_One_Paired_Chip()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "abc";
        vm.EntryEndsText = "xyz";
        vm.EntryIgnoreCase = false;

        Assert.True(vm.AddPatternFromEntry());

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.Equal(string.Empty, vm.EntryText);
        Assert.Equal(string.Empty, vm.EntryEndsText);
    }

    [Fact]
    public void AddPatternFromEntry_With_A_Blank_Second_Half_Adds_Nothing_And_Returns_False()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "abc";

        Assert.False(vm.AddPatternFromEntry());
        Assert.Empty(vm.Patterns);
        Assert.Equal("abc", vm.EntryText);
    }

    [Fact]
    public void Start_With_A_Pending_Pair_Commits_Both_Boxes_And_The_Search_Runs()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryText = "a";
        vm.EntryEndsText = "b";
        vm.Count = 1;

        Assert.True(vm.CanStart);
        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("a", chip.Text);
        Assert.Equal("b", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.Equal(string.Empty, vm.EntryText);
        Assert.Equal(string.Empty, vm.EntryEndsText);
        Assert.Single(vm.Results);
    }

    [Fact]
    public void A_Paired_Chip_Round_Trips_Through_Settings()
    {
        MemorySettingsStore settings = new();
        MemoryResultStore results = new();
        FakeNotifications notes = new();
        SearchViewModel first = new(settings, results, notes, () => Array.Empty<DeviceCatalogEntry>());
        first.AddPair("abc", "xyz", ignoreCase: false);

        SearchViewModel restored = new(settings, results, notes, () => Array.Empty<DeviceCatalogEntry>());

        PatternChip chip = Assert.Single(restored.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Null(chip.Warning);
        Assert.True(restored.CanStart);
    }

    [Fact]
    public void A_Pair_Draft_Without_A_Second_Half_Is_Skipped_On_Restore()
    {
        // A settings file written by an older build (or corrupted) carries no second half; the
        // chip is dropped rather than restored as a rule it cannot express.
        MemorySettingsStore settings = new();
        settings.Save(new AppSettings
        {
            Patterns = new[] { new PatternDraft("abc", "Pair", true), new PatternDraft("ab", "Starts", true) },
        });

        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("ab", chip.Text);
        Assert.Equal(MatchPosition.Starts, chip.Position);
    }

    [Fact]
    public void CanStart_Is_True_With_Reachable_Pending_Text_And_No_Chips()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryText = "ab";
        Assert.Empty(vm.Patterns);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public void PatternProblem_Is_The_Generic_Message_For_A_Blank_Entry_And_No_Chips()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.Equal(UiText.Of(SearchText.AddAtLeastOnePattern), vm.PatternProblem);
    }

    [Fact]
    public void PatternProblem_Names_The_Typed_Text_For_An_Unreachable_Entry()
    {
        (SearchViewModel vm, _, _, _) = Create();
        // "@" is not in the XRPL base58 alphabet and stays unreachable after normalization
        // (unlike "0", which the normalizer folds onto "o" and which would then be reachable).
        vm.EntryText = "@";
        Assert.Equal(UiText.Of(SearchText.PatternUnreachable, ("pattern", "@")), vm.PatternProblem);
    }

    [Fact]
    public void PatternProblem_Is_Null_Once_A_Reachable_Chip_Exists()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, false);
        Assert.Null(vm.PatternProblem);
    }

    [Fact]
    public void PatternHint_Tells_The_User_To_Press_Enter_When_The_Entry_Is_Reachable_And_No_Chip_Exists()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.EntryText = "ab";
        Assert.Equal(UiText.Of(SearchText.PatternPressEnter, ("typed", "ab")), vm.PatternHint);
    }

    [Fact]
    public void Live_Lists_Can_Be_Read_Concurrently_With_A_Running_Search()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        Exception? failure = null;
        Stopwatch sw = Stopwatch.StartNew();
        Thread reader = new(() =>
        {
            try
            {
                while (sw.Elapsed < TimeSpan.FromMilliseconds(300))
                {
                    _ = vm.Results.Count;
                    _ = vm.Targets.Count;
                    _ = vm.Patterns.Count;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        reader.Start();

        while (sw.Elapsed < TimeSpan.FromMilliseconds(300))
        {
            vm.SetTargetSelected("cpu", true);
            vm.RefreshDevices();
        }

        reader.Join();
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");

        Assert.Null(failure);
    }

    [Fact]
    public void MeasureSpeed_Measures_The_Cpu_Without_Touching_Results_History_Or_Notifications()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, MemorySettingsStore settings) = Create();
        vm.CalibrationDuration = TimeSpan.FromMilliseconds(300);
        vm.AddPattern("a", MatchPosition.Starts, false);   // present so CanStart is otherwise true

        Assert.True(vm.CanMeasureSpeed);
        Assert.True(vm.CanStart);

        vm.MeasureSpeed();

        Assert.True(vm.IsCalibrating);
        Assert.False(vm.CanMeasureSpeed);
        Assert.False(vm.CanStart);   // Start refuses while a calibration is running

        // OnCalibrationCompleted clears IsCalibrating before it writes the hint, so the flag alone
        // does not mean the measurement has landed.
        WaitUntil(() => settings.Current.RateHints.ContainsKey("cpu"), "rate hint stored");

        Assert.False(vm.IsCalibrating);
        Assert.Null(vm.CalibrationFailure);
        Assert.Empty(vm.Results);
        Assert.Null(vm.LastSummary);
        Assert.Equal(0, results.Appends);
        Assert.Equal(0, results.AppendRanges);
        Assert.Empty(notes.Sent);
        Assert.NotNull(vm.EstimatedTime);   // now computed from the freshly measured hint
    }

    [Fact]
    public void Start_During_A_Calibration_Does_Nothing()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.CalibrationDuration = TimeSpan.FromSeconds(5);   // long enough to still be running below
        vm.AddPattern("a", MatchPosition.Starts, false);

        vm.MeasureSpeed();
        Assert.True(vm.IsCalibrating);

        vm.Start();

        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Null(vm.LastSummary);

        vm.Dispose();   // stop the still-running calibration
    }

    [Fact]
    public void CanMeasureSpeed_Is_False_While_A_Real_Search_Runs_And_MeasureSpeed_Refuses()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        Assert.False(vm.CanMeasureSpeed);
        vm.MeasureSpeed();
        Assert.False(vm.IsCalibrating);   // refused: a real search is running

        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
    }
}
