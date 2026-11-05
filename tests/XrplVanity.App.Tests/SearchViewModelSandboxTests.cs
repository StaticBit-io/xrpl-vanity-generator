using System.Diagnostics;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchViewModelSandboxTests
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

    private static void RunToCompletion(SearchViewModel vm)
    {
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
    }

    [Fact]
    public void Begin_Empties_The_Patterns_And_Stops_At_One_Match()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        vm.Count = 5;

        Assert.True(vm.BeginSandbox());

        Assert.True(vm.IsSandbox);
        Assert.Empty(vm.Patterns);
        Assert.Equal(1, vm.Count);
    }

    [Fact]
    public void Begin_Is_Refused_While_A_Search_Runs()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        Assert.False(vm.BeginSandbox());

        Assert.False(vm.IsSandbox);
        Assert.Single(vm.Patterns);
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
    }

    [Fact]
    public void Begin_Is_Refused_While_A_Speed_Measurement_Runs()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.CalibrationDuration = TimeSpan.FromSeconds(5);
        vm.MeasureSpeed();
        Assert.True(vm.IsCalibrating);

        Assert.False(vm.BeginSandbox());

        Assert.False(vm.IsSandbox);
        vm.Dispose();
    }

    [Fact]
    public void Begin_Is_Refused_Inside_A_Sandbox_And_Keeps_The_First_Snapshot()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("tutor", MatchPosition.Starts, false);

        Assert.False(vm.BeginSandbox());
        vm.EndSandbox();

        PatternChip restored = Assert.Single(vm.Patterns);
        Assert.Equal("user", restored.Text);
    }

    [Fact]
    public void Sandbox_Edits_Never_Reach_The_Settings_Store()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        vm.CpuThreads = 1;
        vm.Count = 4;
        vm.DutyPercent = 70;
        AppSettings before = settings.Load();
        int savesBefore = settings.Saves;
        Assert.True(vm.BeginSandbox());

        vm.AddPattern("tutor", MatchPosition.Starts, true);
        vm.AddPair("ab", "cd", true);
        vm.SetTargetSelected("cpu", false);
        vm.SetTargetSelected("cpu", true);
        vm.DutyPercent = 25;
        vm.Count = 3;
        vm.CpuThreads = Math.Max(1, Environment.ProcessorCount);
        vm.EntryText = "draft";
        vm.ClearPatterns();

        Assert.Equal(before, settings.Load());
        Assert.Equal(savesBefore, settings.Saves);
        Assert.Equal(25, vm.DutyPercent);   // the live value still moves
    }

    [Fact]
    public void Sandbox_Matches_Reach_Neither_Store_Nor_Notifications()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, _) = Create();
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("a", MatchPosition.Starts, false);

        RunToCompletion(vm);

        SessionResult hit = Assert.Single(vm.Results);
        Assert.StartsWith("ra", hit.Address);
        Assert.Empty(results.LoadAll());
        Assert.Equal(0, results.AppendRanges);
        Assert.Equal(0, results.Appends);
        Assert.Empty(notes.Sent);
    }

    [Fact]
    public void Rate_Hints_Written_In_A_Sandbox_Keep_The_Users_Patterns()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        vm.Count = 9;
        PatternDraft userDraft = Assert.Single(settings.Load().Patterns);
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("a", MatchPosition.Starts, false);

        RunToCompletion(vm);

        Assert.True(settings.Current.RateHints.ContainsKey("cpu"), "the measured speed is kept");
        Assert.Equal(userDraft, Assert.Single(settings.Current.Patterns));
        Assert.Equal(9, settings.Current.Count);
    }

    [Fact]
    public void End_Restores_Every_Live_Field()
    {
        DeviceCatalogEntry gpu = new(new DeviceSelector("cuda", 0), "RTX", "sm_120", false);
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create(gpu);
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 2;
        RunToCompletion(vm);
        SessionResult[] userResults = vm.Results.ToArray();
        SearchSummary userSummary = vm.LastSummary!;
        Assert.Equal(2, userResults.Length);

        vm.AddPattern("user", MatchPosition.Ends, true);
        vm.AddPair("ab", "cd", false);
        vm.SetTargetSelected("cuda:0", true);
        vm.SetTargetSelected("cpu", false);
        vm.CpuThreads = 1;
        vm.Count = 7;
        vm.DutyPercent = 60;
        vm.EntryText = "pending";
        vm.EntryEndsText = "tail";
        vm.EntryPosition = MatchPosition.Pair;
        vm.EntryIgnoreCase = false;
        vm.EntryNearStart = true;
        string[] chipsBefore = vm.Patterns.Select(p => p.Identity).ToArray();
        AppSettings storedBefore = settings.Load();

        Assert.True(vm.BeginSandbox());
        Assert.Equal(string.Empty, vm.EntryText);
        vm.AddPattern("a", MatchPosition.Starts, true);
        vm.SetTargetSelected("cuda:0", false);
        vm.SetTargetSelected("cpu", true);
        vm.CpuThreads = Math.Max(1, Environment.ProcessorCount);
        vm.DutyPercent = 30;
        vm.EntryText = "@";   // unreachable, so Start does not commit it as a chip
        vm.EntryPosition = MatchPosition.Starts;
        vm.EntryIgnoreCase = true;
        vm.EntryNearStart = false;
        RunToCompletion(vm);
        Assert.Single(vm.Results);

        vm.EndSandbox();

        Assert.False(vm.IsSandbox);
        Assert.Equal(chipsBefore, vm.Patterns.Select(p => p.Identity).ToArray());
        Assert.Equal(new[] { "cuda:0" }, vm.Targets.Where(t => t.Selected).Select(t => t.Id).ToArray());
        Assert.Equal(1, vm.CpuThreads);
        Assert.Equal(7, vm.Count);
        Assert.Equal(60, vm.DutyPercent);
        Assert.Equal("pending", vm.EntryText);
        Assert.Equal("tail", vm.EntryEndsText);
        Assert.Equal(MatchPosition.Pair, vm.EntryPosition);
        Assert.False(vm.EntryIgnoreCase);
        Assert.True(vm.EntryNearStart);
        Assert.Equal(userResults.Select(r => r.Seed), vm.Results.Select(r => r.Seed));
        Assert.Same(userSummary, vm.LastSummary);
        Assert.Equal(storedBefore.Patterns, settings.Load().Patterns);
        Assert.Equal(storedBefore.DutyPercent, settings.Load().DutyPercent);
    }

    [Fact]
    public void Begin_Hides_The_Users_Earlier_Results_And_Summary_And_End_Brings_Them_Back()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 2;
        RunToCompletion(vm);
        string[] userSeeds = vm.Results.Select(r => r.Seed).ToArray();
        SearchSummary userSummary = vm.LastSummary!;
        Assert.Equal(2, userSeeds.Length);

        Assert.True(vm.BeginSandbox());

        Assert.Empty(vm.Results);
        Assert.Null(vm.LastSummary);
        Assert.Null(vm.Progress);
        Assert.Null(vm.LastFailure);

        vm.EndSandbox();

        Assert.Equal(userSeeds, vm.Results.Select(r => r.Seed).ToArray());
        Assert.Same(userSummary, vm.LastSummary);
    }

    [Fact]
    public void End_During_A_Search_Stops_It_And_Restores_On_Completion()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, _) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        vm.DutyPercent = 80;
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.DutyPercent = 20;
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        vm.EndSandbox();

        WaitUntil(() => !vm.IsSandbox, "the sandbox to end");
        Assert.Equal(SearchState.Idle, vm.State);
        PatternChip restored = Assert.Single(vm.Patterns);
        Assert.Equal("user", restored.Text);
        Assert.Equal(80, vm.DutyPercent);
        Assert.Empty(notes.Sent);
        Assert.Empty(results.LoadAll());
    }

    [Fact]
    public void End_During_A_Speed_Measurement_Stops_It_And_Restores_On_Completion()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        Assert.True(vm.BeginSandbox());
        vm.CalibrationDuration = TimeSpan.FromSeconds(30);
        vm.MeasureSpeed();
        Assert.True(vm.IsCalibrating);

        vm.EndSandbox();

        WaitUntil(() => !vm.IsSandbox, "the sandbox to end");
        Assert.False(vm.IsCalibrating);
        PatternChip restored = Assert.Single(vm.Patterns);
        Assert.Equal("user", restored.Text);
        Assert.Empty(settings.Current.RateHints);   // a measurement cut short is not recorded
    }

    [Fact]
    public void End_While_The_Search_Is_Still_Starting_Restores_On_Completion()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        vm.Count = 7;
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        Assert.NotEqual(SearchState.Idle, vm.State);

        vm.EndSandbox();

        WaitUntil(() => !vm.IsSandbox, "the sandbox to end");
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Equal("user", Assert.Single(vm.Patterns).Text);
        Assert.Equal(7, vm.Count);
    }

    [Fact]
    public void A_Second_End_During_The_Same_Run_Changes_Nothing()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("user", MatchPosition.Starts, false);
        Assert.True(vm.BeginSandbox());
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");

        vm.EndSandbox();
        vm.EndSandbox();

        WaitUntil(() => !vm.IsSandbox, "the sandbox to end");
        vm.EndSandbox();   // and a third one after the restore is a no-op too
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Equal("user", Assert.Single(vm.Patterns).Text);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public void A_Calibration_Failure_Is_Hidden_In_The_Sandbox_And_Comes_Back()
    {
        // An OpenCL device that does not exist on any machine, so the measurement reports a failure.
        DeviceCatalogEntry ghost = new(new DeviceSelector("opencl", 99), "Ghost", "none", false);
        (SearchViewModel vm, _, _, _) = Create(ghost);
        vm.SetTargetSelected("opencl:99", true);
        vm.SetTargetSelected("cpu", false);
        vm.MeasureSpeed();
        // The failure is written just after the flag drops.
        WaitUntil(() => !vm.IsCalibrating && vm.CalibrationFailure is not null, "the measurement to report its failure");
        string? failure = vm.CalibrationFailure;
        Assert.NotNull(failure);

        Assert.True(vm.BeginSandbox());
        Assert.Null(vm.CalibrationFailure);
        vm.EndSandbox();

        Assert.Equal(failure, vm.CalibrationFailure);
    }

    [Fact]
    public void End_Leaves_No_Tutorial_Result_Behind()
    {
        (SearchViewModel vm, _, MemoryResultStore results, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 2;
        RunToCompletion(vm);
        string[] userSeeds = vm.Results.Select(r => r.Seed).ToArray();
        vm.ClearPatterns();

        Assert.True(vm.BeginSandbox());
        vm.AddPattern("a", MatchPosition.Starts, false);
        RunToCompletion(vm);
        string tutorialSeed = Assert.Single(vm.Results).Seed;
        vm.EndSandbox();

        Assert.Equal(userSeeds, vm.Results.Select(r => r.Seed).ToArray());
        Assert.DoesNotContain(vm.Results, r => r.Seed == tutorialSeed);
        Assert.DoesNotContain(results.LoadAll(), r => r.Seed == tutorialSeed);
        Assert.Equal(2, results.LoadAll().Count);
    }

    [Fact]
    public void A_Match_Arriving_After_End_Is_Not_Persisted()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, _) = Create();
        Assert.True(vm.BeginSandbox());
        vm.EndSandbox();

        vm.OnMatch(new Match("rLate", "sLate", "late", MatchPosition.Starts), sandboxSession: true);
        vm.FlushPendingMatches();

        Assert.Empty(results.LoadAll());
        Assert.Empty(vm.Results);
        Assert.Empty(notes.Sent);
    }

    [Fact]
    public void A_Match_From_An_Ordinary_Session_Is_Still_Persisted()
    {
        (SearchViewModel vm, _, MemoryResultStore results, _) = Create();

        vm.OnMatch(new Match("rReal", "sReal", "real", MatchPosition.Starts), sandboxSession: false);
        vm.FlushPendingMatches();

        Assert.Equal("rReal", Assert.Single(results.LoadAll()).Address);
    }

    [Fact]
    public void Tray_Load_And_Stop_Are_Refused_Only_In_A_Sandbox()
    {
        (SearchViewModel vm, _, _, _) = Create();
        TrayAction[] refused = { TrayAction.Duty25, TrayAction.Duty50, TrayAction.Duty80, TrayAction.Duty100, TrayAction.StopSearch };

        Assert.All(Enum.GetValues<TrayAction>(), a => Assert.True(vm.AcceptsTrayAction(a)));

        Assert.True(vm.BeginSandbox());
        Assert.All(refused, a => Assert.False(vm.AcceptsTrayAction(a)));
        Assert.True(vm.AcceptsTrayAction(TrayAction.Show));
        Assert.True(vm.AcceptsTrayAction(TrayAction.Exit));

        vm.EndSandbox();
        Assert.All(Enum.GetValues<TrayAction>(), a => Assert.True(vm.AcceptsTrayAction(a)));
    }
}
