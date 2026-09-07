using System.IO;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using Xunit;

namespace XrplVanity.App.Tests;

public class JsonSettingsStoreTests
{
    private static string TempFolder() => Directory.CreateTempSubdirectory("xrpl-vanity-settings-").FullName;

    [Fact]
    public void Missing_File_Yields_Defaults()
    {
        JsonSettingsStore store = new(TempFolder());
        AppSettings settings = store.Load();
        Assert.Equal(100, settings.DutyPercent);
        Assert.Equal(CloseBehavior.Ask, settings.CloseBehavior);
        Assert.Equal(30, settings.ClipboardClearSeconds);
        Assert.Equal(15, settings.SeedRevealSeconds);
        Assert.True(settings.NotificationsEnabled);
        Assert.Empty(settings.RateHints);
    }

    [Fact]
    public void Round_Trips_Every_Field_Atomically()
    {
        string folder = TempFolder();
        JsonSettingsStore store = new(folder);
        AppSettings settings = store.Load() with
        {
            DutyPercent = 80,
            CloseBehavior = CloseBehavior.MinimizeToTray,
            Patterns = new[] { new PatternDraft("abc", "Starts", true), new PatternDraft("abc", "Pair", true, "xyz") },
            Targets = new[] { "cpu", "cuda:0" },
            CpuThreads = 6,
            Count = 3,
            WindowWidth = 1200,
            WindowHeight = 800,
            RateHints = new System.Collections.Generic.Dictionary<string, double> { ["cuda:0"] = 19_400_000 },
        };

        store.Save(settings);

        Assert.Equal(settings, new JsonSettingsStore(folder).Load());
        Assert.False(File.Exists(Path.Combine(folder, "settings.json.tmp")));
    }

    [Fact]
    public void A_Pattern_Written_Before_The_Second_Half_Existed_Still_Loads()
    {
        string folder = TempFolder();
        File.WriteAllText(
            Path.Combine(folder, "settings.json"),
            "{ \"Patterns\": [ { \"Text\": \"abc\", \"Position\": \"Starts\", \"IgnoreCase\": true } ] }");

        PatternDraft draft = Assert.Single(new JsonSettingsStore(folder).Load().Patterns);
        Assert.Equal("abc", draft.Text);
        Assert.Equal("Starts", draft.Position);
        Assert.Null(draft.EndsText);
    }

    [Fact]
    public void A_Corrupted_File_Falls_Back_To_Defaults()
    {
        string folder = TempFolder();
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ not json");
        Assert.Equal(100, new JsonSettingsStore(folder).Load().DutyPercent);
    }
}
