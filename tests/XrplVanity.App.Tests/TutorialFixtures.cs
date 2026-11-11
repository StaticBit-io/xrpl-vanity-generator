using System.Diagnostics;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

/// <summary>Drives a real <see cref="TutorialService"/> to the steps the page tests need, without a dialog or a real match.</summary>
internal static class TutorialFixtures
{
    private const string LongWord = "abcdefhjkm";

    /// <summary>Pins the estimate through a rate hint; the device refresh stands in for the notification a measurement raises.</summary>
    internal static void SetEstimate(SearchViewModel vm, TimeSpan estimate)
    {
        double candidates = vm.ExpectedCandidates!.Value;
        double rate = candidates / estimate.TotalSeconds;
        Dictionary<string, double> hints = vm.Targets.Where(t => t.Selected).ToDictionary(t => t.Id, t => rate);
        vm.SaveSettings(s => s with { RateHints = hints });
        vm.RefreshDevices();
    }

    /// <summary>Begins the tutorial and plays it to the Result step, with one injected match in the sandbox results.</summary>
    internal static void ReachResult(SearchViewModel vm, TutorialService tutorial)
    {
        Assert.True(tutorial.Begin());
        Assert.True(vm.AddPattern(LongWord, MatchPosition.Starts, true));
        tutorial.Next();
        SetEstimate(vm, TimeSpan.FromDays(2));
        tutorial.Next();
        tutorial.Next();
        Assert.Equal(TutorialStep.ShortWord, tutorial.Current!.Step);

        vm.ClearPatterns();
        Assert.True(vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false));
        SetEstimate(vm, TimeSpan.FromSeconds(5));
        Assert.Equal(TutorialStep.Search, tutorial.Current!.Step);

        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");
        vm.OnMatch(new Match("rTutorial", "sTutorial", "zzzzzzzz", MatchPosition.Starts), sandboxSession: true);
        vm.FlushPendingMatches();
        vm.Stop();
        // Idle is set before the view model raises Changed, which is what moves the tutorial on.
        WaitUntil(() => vm.State == SearchState.Idle && tutorial.Current!.Step == TutorialStep.Result, "the tutorial to reach the result");
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(120)) Thread.Sleep(25);
        Assert.True(condition(), $"timed out waiting for {what}");
    }
}
