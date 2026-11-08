using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class EstimateCardTests : BunitContext
{
    [Fact]
    public void Without_Patterns_It_Invites_To_Add_One()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("add a pattern to see an estimate", cut.Markup);
    }

    [Fact]
    public void With_A_Pattern_And_No_Rate_It_Shows_Candidates_And_Rate_Unknown()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        vm.AddPattern("abcd", MatchPosition.Starts, false);
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));

        // Base58 leading-digit model (SearchEstimator), not a naive 58^-n count: "Starts abcd" is
        // about 4.55 M candidates, which Formatting.Candidates renders as "4.6 M".
        double expectedCandidates = ExpectedCandidatesFor(MatchPosition.Starts, "abcd");
        Assert.Equal("4.6 M", Formatting.Candidates(expectedCandidates));
        Assert.Equal(Formatting.Candidates(expectedCandidates), cut.Find(".candidates").TextContent.Trim());
        Assert.Equal("—", cut.Find(".eta").TextContent.Trim());
        Assert.Contains("rate unknown — press \"measure speed\" or run a search once", cut.Markup);
    }

    [Fact]
    public void With_A_Rate_Hint_It_Shows_The_Eta()
    {
        MemorySettingsStore settings = new();
        settings.Save(settings.Load() with { RateHints = new Dictionary<string, double> { ["cpu"] = 1000 } });
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        vm.AddPattern("ab", MatchPosition.Starts, false); // ~1,354 candidates / 1,000 per s ≈ 1.35 s -> "1 s"
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));

        double expectedCandidates = ExpectedCandidatesFor(MatchPosition.Starts, "ab");
        TimeSpan? expectedEta = SearchEstimator.ExpectedTimeToMatch(expectedCandidates, 1000);
        Assert.Equal("1 s", Formatting.Duration(expectedEta));
        Assert.Equal(Formatting.Duration(expectedEta), cut.Find(".eta").TextContent.Trim());
    }

    [Theory]
    [InlineData(58, "58")]
    [InlineData(3364, "3,364")]
    [InlineData(11_316_496, "11.3 M")]
    [InlineData(2.2e9, "2.2 G")]
    [InlineData(45_907_500_000_000d, "45.9 T")]
    [InlineData(1e15, "1 P")]
    [InlineData(1.5e18, "1.5 E")]
    [InlineData(2e21, "2e21")]
    public void Formats_Candidates(double value, string expected) => Assert.Equal(expected, Formatting.Candidates(value));

    [Fact]
    public void Formats_Durations()
    {
        Assert.Equal("—", Formatting.Duration(null));
        Assert.Equal("12 s", Formatting.Duration(TimeSpan.FromSeconds(12.4)));
        Assert.Equal("4 min", Formatting.Duration(TimeSpan.FromMinutes(4.2)));
        Assert.Equal("3 h 20 min", Formatting.Duration(TimeSpan.FromMinutes(200)));
        Assert.Equal("2.5 days", Formatting.Duration(TimeSpan.FromHours(60)));
        Assert.Equal("> 1 year", Formatting.Duration(TimeSpan.FromDays(400)));
        Assert.Equal("0 s", Formatting.Duration(TimeSpan.Zero));
        Assert.Equal("< 1 s", Formatting.Duration(TimeSpan.FromMilliseconds(235)));
    }

    private static double ExpectedCandidatesFor(MatchPosition position, string pattern)
    {
        PatternSet.Builder builder = new();
        builder.Add(position, pattern, ignoreCase: false);
        return SearchEstimator.ExpectedCandidatesPerMatch(builder.Build())!.Value;
    }
}
