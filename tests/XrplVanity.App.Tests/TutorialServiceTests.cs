using System.Diagnostics;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class TutorialServiceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    // Ten letters, all inside the address alphabet: the shortest word that stays above a day on the
    // fastest measured GPU.
    private const string LongWord = "abcdefhjkm";

    private sealed class Rig
    {
        public required TutorialService Tutorial { get; init; }
        public required SearchViewModel Vm { get; init; }
        public required MemorySettingsStore Settings { get; init; }
        public required MemoryResultStore Results { get; init; }
        public required FakeNotifications Notes { get; init; }
    }

    private static Rig Create(bool gpu = false, MemoryResultStore? results = null, IResultStore? tutorialStore = null, DeviceCatalogEntry[]? devices = null)
    {
        FakeNotifications notes = new();
        MemoryResultStore store = results ?? new MemoryResultStore();
        MemorySettingsStore settings = new();
        devices ??= gpu
            ? new[] { new DeviceCatalogEntry(new DeviceSelector("cuda", 0), "RTX", "sm_120", false) }
            : Array.Empty<DeviceCatalogEntry>();
        SearchViewModel vm = new(settings, store, notes, () => devices);
        TutorialService tutorial = new(vm, tutorialStore ?? store);
        return new Rig { Tutorial = tutorial, Vm = vm, Settings = settings, Results = store, Notes = notes };
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < Deadline) Thread.Sleep(25);
        Assert.True(condition(), $"timed out waiting for {what}");
    }

    /// <summary>
    /// Pins the estimate to a known value through a rate hint. SaveSettings raises no event, so the
    /// device refresh stands in for the change notification a measurement would raise.
    /// </summary>
    private static void SetEstimate(Rig rig, TimeSpan estimate)
    {
        double candidates = rig.Vm.ExpectedCandidates!.Value;
        double rate = candidates / estimate.TotalSeconds;
        Dictionary<string, double> hints = rig.Vm.Targets.Where(t => t.Selected).ToDictionary(t => t.Id, t => rate);
        rig.Vm.SaveSettings(s => s with { RateHints = hints });
        rig.Vm.RefreshDevices();
    }

    private static TutorialStep StepOf(Rig rig) => rig.Tutorial.Current!.Step;

    /// <summary>Drives the tutorial the way a user would, one step at a time, until it reaches <paramref name="target"/>.</summary>
    private static void Reach(Rig rig, TutorialStep target)
    {
        if (!rig.Tutorial.IsActive) Assert.True(rig.Tutorial.Begin());

        for (int guard = 0; guard < 40 && StepOf(rig) != target; guard++)
        {
            switch (StepOf(rig))
            {
                case TutorialStep.AddWord:
                    Assert.True(rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true));
                    break;
                case TutorialStep.Devices:
                    SetEstimate(rig, TimeSpan.FromDays(2));
                    rig.Tutorial.Next();
                    break;
                case TutorialStep.ShortWord:
                    Assert.True(rig.Vm.AddPattern("ab", MatchPosition.Starts, true));
                    SetEstimate(rig, TimeSpan.FromSeconds(5));
                    break;
                case TutorialStep.Search:
                    rig.Vm.Start();
                    WaitUntil(() => StepOf(rig) != TutorialStep.Search, "the search to produce a result");
                    break;
                case TutorialStep.QrButton:
                    rig.Tutorial.QrOpened();
                    break;
                case TutorialStep.QrDialog:
                    rig.Tutorial.QrClosed();
                    break;
                case TutorialStep.GpuDuty:
                    rig.Vm.DutyPercent = rig.Vm.DutyPercent == 50 ? 60 : 50;
                    break;
                default:
                    rig.Tutorial.Next();
                    break;
            }
        }

        Assert.Equal(target, StepOf(rig));
    }

    private sealed class ThrowingStore : IResultStore
    {
        public bool IsReadOnly => false;
        public string? ReadOnlyReason => null;
        public IReadOnlyList<StoredResult> LoadAll() => throw new IOException("results.dat is unreadable");
        public void Append(StoredResult result) { }
        public void AppendRange(IEnumerable<StoredResult> results) { }
        public void Delete(string address) { }
        public void DeleteMany(IEnumerable<string> addresses) { }
        public void Clear() { }
    }

    [Fact]
    public void ShouldOffer_Only_With_No_Flag_No_Patterns_And_No_Results()
    {
        Assert.True(Create().Tutorial.ShouldOffer());

        Rig offered = Create();
        offered.Vm.SaveSettings(s => s with { TutorialOffered = true });
        Assert.False(offered.Tutorial.ShouldOffer());

        Rig withPattern = Create();
        withPattern.Vm.AddPattern("user", MatchPosition.Starts, false);
        Assert.False(withPattern.Tutorial.ShouldOffer());

        MemoryResultStore stored = new();
        stored.Append(new StoredResult("rOld", "sOld", "old", "Starts", DateTimeOffset.Now, "cpu"));
        Assert.False(Create(results: stored).Tutorial.ShouldOffer());
    }

    [Fact]
    public void ShouldOffer_Is_False_When_The_Store_Is_ReadOnly_Or_Throws()
    {
        Assert.False(Create(results: new MemoryResultStore { IsReadOnly = true, ReadOnlyReason = "locked" }).Tutorial.ShouldOffer());
        Assert.False(Create(tutorialStore: new ThrowingStore()).Tutorial.ShouldOffer());
    }

    [Fact]
    public void ShouldOffer_Is_False_For_A_Store_That_Only_Turns_ReadOnly_While_Loading()
    {
        // The real store flips IsReadOnly inside LoadAll and returns an empty list, so the flag has to
        // be read after the load, not before it.
        string folder = Path.Combine(Path.GetTempPath(), "tutorial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "results.dat"), new byte[] { 1, 2, 3, 4, 5 });
            DpapiResultStore store = new(folder);
            Assert.False(store.IsReadOnly);

            Assert.False(Create(tutorialStore: store).Tutorial.ShouldOffer());
            Assert.True(store.IsReadOnly);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Begin_Starts_The_Sandbox_And_Records_The_Offer()
    {
        Rig rig = Create();
        rig.Tutorial.ShowOffer();
        Assert.Equal(TutorialStep.Offer, StepOf(rig));

        Assert.True(rig.Tutorial.Begin());

        Assert.True(rig.Vm.IsSandbox);
        Assert.True(rig.Tutorial.IsActive);
        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
        Assert.True(rig.Settings.Current.TutorialOffered);
    }

    [Fact]
    public void Showing_The_Offer_Records_It_Before_The_User_Answers()
    {
        Rig rig = Create();
        Assert.False(rig.Settings.Current.TutorialOffered);

        rig.Tutorial.ShowOffer();

        // A window closed or a crash with the card still open must not bring the offer back.
        Assert.True(rig.Settings.Current.TutorialOffered);
        Assert.False(rig.Tutorial.ShouldOffer());
    }

    [Fact]
    public void Begin_Is_Refused_While_A_Search_Runs_And_Leaves_The_Offer_Open()
    {
        Rig rig = Create();
        rig.Vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        rig.Vm.Start();
        WaitUntil(() => rig.Vm.State == SearchState.Searching, "searching");
        rig.Tutorial.ShowOffer();

        Assert.False(rig.Tutorial.Begin());

        Assert.False(rig.Vm.IsSandbox);
        Assert.Equal(TutorialStep.Offer, StepOf(rig));
        rig.Vm.Stop();
        WaitUntil(() => rig.Vm.State == SearchState.Idle, "idle");
    }

    [Fact]
    public void Begin_Without_An_Offer_Starts_From_The_First_Step()
    {
        Rig rig = Create();
        rig.Vm.AddPattern("user", MatchPosition.Starts, false);

        Assert.True(rig.Tutorial.CanStart);
        Assert.True(rig.Tutorial.Begin());

        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
        Assert.False(rig.Tutorial.CanStart);
        Assert.False(rig.Tutorial.Begin());
    }

    [Fact]
    public void Not_Now_Records_The_Offer_And_Starts_No_Sandbox()
    {
        Rig rig = Create();
        rig.Tutorial.ShowOffer();

        rig.Tutorial.Leave();

        Assert.False(rig.Tutorial.IsActive);
        Assert.Null(rig.Tutorial.Current);
        Assert.False(rig.Vm.IsSandbox);
        Assert.True(rig.Settings.Current.TutorialOffered);
    }

    [Fact]
    public void AddWord_Moves_On_When_A_Pattern_Exists()
    {
        Rig rig = Create();
        Assert.True(rig.Tutorial.Begin());
        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
        Assert.False(rig.Tutorial.CanGoNext);

        rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true);

        Assert.Equal(TutorialStep.ListFiles, StepOf(rig));
        Assert.True(rig.Tutorial.CanGoNext);
    }

    [Fact]
    public void An_Unreachable_Word_Does_Not_Move_The_Tutorial_On()
    {
        Rig rig = Create();
        Assert.True(rig.Tutorial.Begin());

        Assert.True(rig.Vm.AddPattern("привет-мир-вокруг", MatchPosition.Starts, true));

        // No estimate can ever come from a chip that no address can match, and Devices does not light
        // the pattern card, so the step must wait for a word that can.
        Assert.Null(rig.Vm.ExpectedCandidates);
        Assert.Equal(TutorialStep.AddWord, StepOf(rig));

        rig.Vm.ClearPatterns();
        Assert.True(rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true));
        Assert.Equal(TutorialStep.ListFiles, StepOf(rig));
    }

    [Fact]
    public void ListFiles_Goes_Back_To_AddWord_When_No_Reachable_Word_Is_Left()
    {
        Rig rig = Create();
        Assert.True(rig.Tutorial.Begin());
        Assert.True(rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true));
        Assert.Equal(TutorialStep.ListFiles, StepOf(rig));
        Assert.True(rig.Tutorial.CanGoNext);

        rig.Vm.ClearPatterns();

        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
        Assert.False(rig.Tutorial.CanGoNext);

        Assert.True(rig.Vm.AddPattern("привет-мир-вокруг", MatchPosition.Starts, true));
        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
    }

    [Fact]
    public void An_Unreachable_Chip_Beside_A_Reachable_One_Still_Gives_Devices_An_Estimate()
    {
        Rig rig = Create();
        Assert.True(rig.Tutorial.Begin());
        Assert.True(rig.Vm.AddPattern("привет-мир-вокруг", MatchPosition.Starts, true));
        Assert.True(rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true));
        Assert.Equal(TutorialStep.ListFiles, StepOf(rig));

        rig.Tutorial.Next();

        Assert.Equal(TutorialStep.Devices, StepOf(rig));
        Assert.NotNull(rig.Vm.ExpectedCandidates);
        SetEstimate(rig, TimeSpan.FromDays(2));
        Assert.True(rig.Tutorial.CanGoNext);
    }

    [Fact]
    public void Devices_Next_Waits_For_An_Estimate()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.Devices);
        Assert.False(rig.Tutorial.CanGoNext);
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.Devices, StepOf(rig));

        SetEstimate(rig, TimeSpan.FromDays(2));

        Assert.True(rig.Tutorial.CanGoNext);
        Assert.Equal(TutorialStep.Devices, StepOf(rig));
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.LongWord, StepOf(rig));
    }

    [Fact]
    public void Devices_Moves_On_When_A_Speed_Measurement_Ends_With_An_Estimate()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.Devices);
        rig.Vm.CalibrationDuration = TimeSpan.FromSeconds(1);

        rig.Vm.MeasureSpeed();

        WaitUntil(() => StepOf(rig) != TutorialStep.Devices, "the measurement to move the tutorial on");
        Assert.Equal(TutorialStep.LongWord, StepOf(rig));
        Assert.NotNull(rig.Vm.EstimatedTime);
    }

    [Fact]
    public void LongWord_Next_Needs_An_Estimate_Above_One_Day()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.LongWord);

        SetEstimate(rig, TimeSpan.FromHours(23));
        Assert.False(rig.Tutorial.CanGoNext);
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.LongWord, StepOf(rig));

        SetEstimate(rig, TimeSpan.FromHours(25));
        Assert.True(rig.Tutorial.CanGoNext);
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.ShortWord, StepOf(rig));
    }

    [Fact]
    public void ShortWord_Moves_On_At_Ten_Seconds()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.ShortWord);

        SetEstimate(rig, TimeSpan.FromSeconds(11));
        Assert.Equal(TutorialStep.ShortWord, StepOf(rig));

        SetEstimate(rig, TimeSpan.FromSeconds(10));
        Assert.Equal(TutorialStep.Search, StepOf(rig));
    }

    [Fact]
    public void Search_Returns_To_ShortWord_When_Idle_And_The_Estimate_Is_Too_Long_Again()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.ShortWord);
        SetEstimate(rig, TimeSpan.FromSeconds(5));
        Assert.Equal(TutorialStep.Search, StepOf(rig));

        SetEstimate(rig, TimeSpan.FromMinutes(1));
        Assert.Equal(TutorialStep.ShortWord, StepOf(rig));

        SetEstimate(rig, TimeSpan.FromSeconds(5));
        Assert.Equal(TutorialStep.Search, StepOf(rig));
    }

    [Fact]
    public void Search_Moves_To_Result_Only_When_Idle_With_A_Result()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.ShortWord);
        rig.Vm.ClearPatterns();
        rig.Vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        SetEstimate(rig, TimeSpan.FromSeconds(5));
        Assert.Equal(TutorialStep.Search, StepOf(rig));
        rig.Vm.Start();
        WaitUntil(() => rig.Vm.State == SearchState.Searching, "searching");

        rig.Vm.OnMatch(new Match("rTutorial", "sTutorial", "zzzzzzzz", MatchPosition.Starts), sandboxSession: true);
        rig.Vm.FlushPendingMatches();
        Assert.NotEmpty(rig.Vm.Results);
        Assert.Equal(TutorialStep.Search, StepOf(rig));

        rig.Vm.Stop();
        WaitUntil(() => rig.Vm.State == SearchState.Idle && StepOf(rig) == TutorialStep.Result, "the tutorial to reach the result");

        Assert.Equal(TutorialStep.Result, StepOf(rig));
    }

    [Fact]
    public void A_Failed_Search_Stays_On_Search_And_Leave_Still_Works()
    {
        // An OpenCL device that does not exist on any machine, so no target can start.
        DeviceCatalogEntry ghost = new(new DeviceSelector("opencl", 99), "Ghost", "none", false);
        Rig rig = Create(devices: new[] { ghost });
        Reach(rig, TutorialStep.ShortWord);
        rig.Vm.SetTargetSelected("opencl:99", true);
        rig.Vm.SetTargetSelected("cpu", false);
        SetEstimate(rig, TimeSpan.FromSeconds(5));
        Assert.Equal(TutorialStep.Search, StepOf(rig));

        rig.Vm.Start();
        WaitUntil(() => rig.Vm.State == SearchState.Idle && (rig.Vm.LastFailure is not null || rig.Vm.LastSummary is not null), "the search to fail");

        Assert.NotNull(rig.Vm.LastFailure);
        Assert.Empty(rig.Vm.Results);
        Assert.Equal(TutorialStep.Search, StepOf(rig));

        rig.Tutorial.Leave();

        Assert.False(rig.Tutorial.IsActive);
        Assert.False(rig.Vm.IsSandbox);
    }

    [Fact]
    public void Result_Qr_Steps_Follow_The_Dialog()
    {
        Rig rig = Create(gpu: true);
        Reach(rig, TutorialStep.Result);

        rig.Tutorial.QrOpened();   // not yet: the button step has not come
        Assert.Equal(TutorialStep.Result, StepOf(rig));

        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.QrButton, StepOf(rig));
        rig.Tutorial.QrClosed();   // a close without an open changes nothing
        Assert.Equal(TutorialStep.QrButton, StepOf(rig));

        rig.Tutorial.QrOpened();
        Assert.Equal(TutorialStep.QrDialog, StepOf(rig));
        rig.Tutorial.QrClosed();
        Assert.Equal(TutorialStep.GpuDuty, StepOf(rig));
    }

    [Fact]
    public void GpuDuty_Moves_On_When_The_Load_Changes()
    {
        Rig rig = Create(gpu: true);
        Reach(rig, TutorialStep.GpuDuty);
        Assert.False(rig.Tutorial.CanGoNext);

        rig.Vm.DutyPercent = rig.Vm.DutyPercent - 10;

        Assert.Equal(TutorialStep.StopAfter, StepOf(rig));
    }

    [Fact]
    public void GpuDuty_Is_Left_Out_With_No_Gpu_Target()
    {
        Rig rig = Create(gpu: false);
        Reach(rig, TutorialStep.QrDialog);
        int countBefore = rig.Tutorial.StepCount;

        rig.Tutorial.QrClosed();

        Assert.Equal(TutorialStep.StopAfter, StepOf(rig));
        Assert.Equal(TutorialScript.Steps.Count - 2, countBefore);

        Rig withGpu = Create(gpu: true);
        Reach(withGpu, TutorialStep.QrDialog);
        withGpu.Tutorial.QrClosed();
        Assert.Equal(TutorialStep.GpuDuty, StepOf(withGpu));
        Assert.Equal(TutorialScript.Steps.Count - 1, withGpu.Tutorial.StepCount);
    }

    [Fact]
    public void The_Settings_Steps_Run_To_Done_And_Finish_Ends_The_Tutorial()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.StopAfter);
        Assert.Equal("/settings", rig.Tutorial.Current!.Route);

        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.NotificationsSetting, StepOf(rig));
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.DataFolder, StepOf(rig));
        rig.Tutorial.Next();
        Assert.Equal(TutorialStep.Done, StepOf(rig));
        Assert.Equal("/", rig.Tutorial.Current!.Route);
        Assert.Equal(rig.Tutorial.StepCount, rig.Tutorial.StepNumber);

        rig.Tutorial.Next();

        Assert.False(rig.Tutorial.IsActive);
        Assert.False(rig.Vm.IsSandbox);
    }

    [Fact]
    public void Inert_Is_True_For_Unlit_And_For_Lit_On_A_Non_Interactive_Step()
    {
        Rig rig = Create();
        Assert.False(rig.Tutorial.IsInert("start"));   // nothing is dimmed while no tutorial runs
        Assert.False(rig.Tutorial.IsLit("patterns"));

        rig.Tutorial.ShowOffer();
        Assert.True(rig.Tutorial.IsInert("patterns"));
        Assert.False(rig.Tutorial.IsLit("patterns"));

        Assert.True(rig.Tutorial.Begin());
        Assert.True(rig.Tutorial.IsLit("patterns"));
        Assert.False(rig.Tutorial.IsInert("patterns"));
        Assert.True(rig.Tutorial.IsInert("start"));

        Reach(rig, TutorialStep.Result);
        Assert.True(rig.Tutorial.IsLit("results"));
        Assert.True(rig.Tutorial.IsInert("results"));
        Assert.True(rig.Tutorial.IsInert("patterns"));

        Reach(rig, TutorialStep.QrButton);
        Assert.True(rig.Tutorial.IsLit("results"));
        Assert.False(rig.Tutorial.IsInert("results"));
    }

    public static TheoryData<TutorialStep> AllSteps()
    {
        TheoryData<TutorialStep> data = new();
        foreach (TutorialStep step in Enum.GetValues<TutorialStep>()) data.Add(step);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSteps))]
    public void Leave_Ends_The_Sandbox_From_Any_Step(TutorialStep step)
    {
        Rig rig = Create(gpu: true);
        rig.Vm.AddPattern("user", MatchPosition.Starts, false);
        rig.Vm.Count = 7;
        rig.Vm.DutyPercent = 80;
        AppSettings stored = rig.Settings.Current;
        if (step == TutorialStep.Offer)
        {
            rig.Tutorial.ShowOffer();
        }
        else
        {
            Reach(rig, step);
        }

        rig.Tutorial.Leave();
        rig.Tutorial.Leave();   // a second call changes nothing

        WaitUntil(() => !rig.Vm.IsSandbox, "the sandbox to end");
        Assert.False(rig.Tutorial.IsActive);
        Assert.Null(rig.Tutorial.Current);
        Assert.Equal("user", Assert.Single(rig.Vm.Patterns).Text);
        Assert.Equal(7, rig.Vm.Count);
        Assert.Equal(80, rig.Vm.DutyPercent);
        Assert.Empty(rig.Vm.Results);
        Assert.Empty(rig.Results.LoadAll());
        Assert.Empty(rig.Notes.Sent);
        Assert.Equal(stored.Patterns, rig.Settings.Current.Patterns);
        Assert.Equal(stored.DutyPercent, rig.Settings.Current.DutyPercent);
        Assert.Equal(stored.Count, rig.Settings.Current.Count);
        Assert.True(rig.Settings.Current.TutorialOffered);
    }

    [Fact]
    public void Leave_During_A_Search_Stops_It_And_Restores()
    {
        Rig rig = Create();
        Reach(rig, TutorialStep.ShortWord);
        rig.Vm.ClearPatterns();
        rig.Vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        SetEstimate(rig, TimeSpan.FromSeconds(5));
        rig.Vm.Start();
        WaitUntil(() => rig.Vm.State == SearchState.Searching, "searching");

        rig.Tutorial.Leave();

        WaitUntil(() => !rig.Vm.IsSandbox, "the sandbox to end");
        Assert.Equal(SearchState.Idle, rig.Vm.State);
        Assert.False(rig.Tutorial.IsActive);
    }

    [Fact]
    public void Leave_During_A_Measurement_Keeps_Every_Region_Inert_Until_The_Sandbox_Ends()
    {
        // The measurement stops within milliseconds of the Leave, so the window this checks is real
        // but short: try a few runs until one catches it. Every read below is ordered so that a true
        // IsSandbox at the end implies it was true at the start, which makes each run race-free.
        bool windowSeen = false;
        for (int attempt = 0; attempt < 25 && !windowSeen; attempt++)
        {
            Rig rig = Create();
            int observedEnd = 0;
            rig.Tutorial.Changed += () =>
            {
                if (!rig.Vm.IsSandbox) Volatile.Write(ref observedEnd, 1);
            };
            Assert.True(rig.Tutorial.Begin());
            rig.Vm.MeasureSpeed();
            Assert.True(rig.Vm.IsCalibrating);

            rig.Tutorial.Leave();

            bool leaving = rig.Tutorial.IsLeaving;
            bool inert = rig.Tutorial.IsInert(TutorialRegions.Patterns);
            bool sandbox = rig.Vm.IsSandbox;
            if (sandbox)
            {
                windowSeen = true;
                Assert.True(leaving, "IsLeaving must be true while the sandbox is still on after Leave");
                Assert.True(inert, "every region must stay inert while the sandbox is still on after Leave");
                Assert.False(rig.Tutorial.IsActive);
            }

            WaitUntil(() => !rig.Vm.IsSandbox, "the sandbox to end");
            if (windowSeen)
            {
                WaitUntil(() => Volatile.Read(ref observedEnd) == 1, "Changed to be raised when the sandbox ends");
            }

            Assert.False(rig.Tutorial.IsLeaving);
            Assert.False(rig.Tutorial.IsInert(TutorialRegions.Patterns));
        }

        Assert.True(windowSeen, "no run caught the sandbox still on after Leave");
    }

    [Fact]
    public void IsLeaving_Is_False_Outside_A_Leave()
    {
        Rig rig = Create();
        Assert.False(rig.Tutorial.IsLeaving);

        rig.Tutorial.ShowOffer();
        Assert.False(rig.Tutorial.IsLeaving);

        Assert.True(rig.Tutorial.Begin());
        Assert.False(rig.Tutorial.IsLeaving);

        rig.Tutorial.Leave();
        WaitUntil(() => !rig.Vm.IsSandbox, "the sandbox to end");
        Assert.False(rig.Tutorial.IsLeaving);
    }

    [Fact]
    public void Changed_Is_Raised_On_Every_Move_And_On_Leave()
    {
        Rig rig = Create();
        int raised = 0;
        rig.Tutorial.Changed += () => Interlocked.Increment(ref raised);

        Assert.True(rig.Tutorial.Begin());
        int afterBegin = raised;
        rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true);
        int afterAdd = raised;
        rig.Tutorial.Leave();

        Assert.True(afterBegin > 0);
        Assert.True(afterAdd > afterBegin);
        Assert.True(raised > afterAdd);
    }

    [Fact]
    public void A_Disposed_Service_Stops_Following_The_View_Model()
    {
        Rig rig = Create();
        Assert.True(rig.Tutorial.Begin());
        rig.Tutorial.Dispose();

        rig.Vm.AddPattern(LongWord, MatchPosition.Starts, true);

        Assert.Equal(TutorialStep.AddWord, StepOf(rig));
    }

    [Fact]
    public void The_Script_Lists_Every_Step_In_Order_With_Known_Regions()
    {
        string[] known =
        {
            "title", "nav", "start", "patterns", "estimate", "devices", "duty", "stats", "results",
            "setting-stop", "setting-notifications", "setting-data", "settings-other",
        };

        Assert.Equal(Enum.GetValues<TutorialStep>(), TutorialScript.Steps.Select(s => s.Step).ToArray());
        Assert.All(TutorialScript.Steps, s => Assert.All(s.Lit, region => Assert.Contains(region, known)));
        Assert.All(TutorialScript.Steps, s => Assert.True(s.Route is "/" or "/settings"));
    }

    [Fact]
    public void Copy_Has_No_Long_Dash()
    {
        foreach (TutorialStepInfo info in TutorialScript.Steps)
        {
            Assert.DoesNotContain('—', info.Title + info.Body);
            Assert.DoesNotContain('–', info.Title + info.Body);
            Assert.False(string.IsNullOrWhiteSpace(info.Title));
            Assert.False(string.IsNullOrWhiteSpace(info.Body));
        }
    }
}
