using System;
using System.Collections.Generic;
using System.Linq;
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

    private sealed class FakeBackend : IGpuBackend
    {
        private readonly int _count;
        public FakeBackend(string name, int count) { Name = name; _count = count; }
        public string Name { get; }
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
        {
            GpuDeviceInfo[] list = new GpuDeviceInfo[_count];
            for (int i = 0; i < _count; i++) list[i] = new GpuDeviceInfo(Name, i, $"{Name} device {i}", "fake");
            return list;
        }
        public IGpuDevice Open(int index) => throw new InvalidOperationException("fake");
    }

    [Fact]
    public void ParseDeviceSpec_Handles_All_Backend_Names_Pairs_And_Flat_Indices()
    {
        IGpuBackend[] backends = { new FakeBackend("cuda", 2), new FakeBackend("opencl", 1) };

        Assert.True(SearchRunner.ParseDeviceSpec("all", backends, out List<DeviceSelector> all, out _));
        Assert.Equal(new[] { "cuda:0", "cuda:1", "opencl:0" }, all.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("CUDA", backends, out List<DeviceSelector> cuda, out _));
        Assert.Equal(new[] { "cuda:0", "cuda:1" }, cuda.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("opencl:0, cuda:1", backends, out List<DeviceSelector> pairs, out _));
        Assert.Equal(new[] { "opencl:0", "cuda:1" }, pairs.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("0,2", backends, out List<DeviceSelector> flat, out _));
        Assert.Equal(new[] { "cuda:0", "opencl:0" }, flat.Select(s => s.ToString()));

        Assert.False(SearchRunner.ParseDeviceSpec("vulkan", backends, out _, out string reason1));
        Assert.Contains("vulkan", reason1);
        Assert.False(SearchRunner.ParseDeviceSpec("cuda:7", backends, out _, out string reason2));
        Assert.Contains("cuda:7", reason2);
        Assert.False(SearchRunner.ParseDeviceSpec("9", backends, out _, out string reason3));
        Assert.Contains("9", reason3);
        Assert.False(SearchRunner.ParseDeviceSpec("", backends, out _, out _));
        Assert.False(SearchRunner.ParseDeviceSpec("opencl", new IGpuBackend[] { new FakeBackend("cuda", 1) }, out _, out string reason4));
        Assert.Contains("no matching device", reason4);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void ResolveDevices_All_Includes_Every_Available_Backend(string backend)
    {
        using IGpuDevice probe = GpuTestHelper.RequireDevice(backend);
        probe.Dispose();

        CliOptions options = CliOptions.Parse(new[] { "--gpu-devices", "all", "--starts", "ab" });
        Assert.True(SearchRunner.ResolveDevices(options, out List<DeviceSelector> selectors, out string reason), reason);
        Assert.Contains(selectors, s => s.Backend == backend);
    }
}
