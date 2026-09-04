using System.Collections.Generic;
using XrplVanity.Cli;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuCliTests
{
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void SearchGpu_Returns_Matches_That_Satisfy_The_Same_Pattern_Set(string backend)
    {
        using IGpuDevice probe = GpuTestHelper.RequireDevice(backend); // skips cleanly when unavailable
        probe.Dispose();

        CliOptions options = CliOptions.Parse(new[] { "--gpu", "--backend", backend, "--starts", "ab" });
        PatternSet patterns = SearchRunner.BuildPatternSet(options);

        List<Match> matches = SearchRunner.SearchGpu(options, launches: 1);

        Assert.True(matches.Count > 0, "expected ~1,200 hits for a 2-char case-insensitive prefix in 2^20 candidates");
        Assert.All(matches, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
    }
}
