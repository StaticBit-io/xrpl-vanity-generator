using System.Diagnostics;
using XrplVanity.App.Core.Components;
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
        Assert.Equal("Add at least one reachable pattern.", vm.StartBlockedReason);
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
        Assert.Equal("rate unknown until the first run", vm.EstimateNote);
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
    public void Deselecting_Every_Target_Blocks_Start()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.SetTargetSelected("cpu", false);
        Assert.False(vm.CanStart);
        Assert.Equal("Select the CPU or at least one device.", vm.StartBlockedReason);
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
        Assert.Equal(SessionOutcome.CountReached, vm.LastSummary!.Outcome);
        Assert.Equal(3, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.StartsWith("ra", r.Address));
        Assert.Equal(3, results.LoadAll().Count);
        Assert.Contains(notes.Sent, n => n.Title == "Match found" && n.Body.Contains(vm.Results[0].Address) && !n.Body.Contains(vm.Results[0].Seed));
        Assert.Contains(notes.Sent, n => n.Title == "Search finished" && n.Body.Contains("3"));
        Assert.True(settings.Current.RateHints.ContainsKey("cpu"), "rate hint stored");
        Assert.True(stateChanges > 0);
        Assert.NotNull(vm.EstimatedTime);   // now computed from the hint
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
        MemoryResultStore results = new() { IsReadOnly = true, ReadOnlyReason = "corrupt" };
        SearchViewModel vm = new(new MemorySettingsStore(), results, notes, () => Array.Empty<DeviceCatalogEntry>());
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Single(vm.Results);
        Assert.Equal("corrupt", vm.HistoryReadOnlyReason);
    }
}
