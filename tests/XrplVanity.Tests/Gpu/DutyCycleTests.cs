using System;
using System.Diagnostics;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

[Collection(XrplVanity.Tests.CpuBoundCollection.Name)]
public sealed class DutyCycleTests
{
    [Theory]
    [InlineData(50, 100, 0)]
    [InlineData(50, 50, 50)]
    [InlineData(50, 80, 12.5)]
    [InlineData(50, 25, 150)]
    [InlineData(50, 1, 4950)]
    [InlineData(0, 50, 0)]
    public void PauseAfter_Scales_The_Launch_Time_By_The_Idle_Share(double launchMs, int duty, double expectedPauseMs)
    {
        TimeSpan pause = DutyCycle.PauseAfter(TimeSpan.FromMilliseconds(launchMs), duty);
        Assert.Equal(expectedPauseMs, pause.TotalMilliseconds, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void PauseAfter_Rejects_A_Duty_Outside_1_To_100(int duty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DutyCycle.PauseAfter(TimeSpan.FromMilliseconds(50), duty));
    }

    [Fact]
    public void Pause_Returns_Immediately_When_Already_Cancelled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Stopwatch stopwatch = Stopwatch.StartNew();
        DutyCycle.Pause(TimeSpan.FromSeconds(5), 10, cts.Token); // would be a 45 s pause
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"pause took {stopwatch.Elapsed}");
    }

    [Fact]
    public void Pause_Wakes_Up_When_Cancelled_Mid_Sleep()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));

        Stopwatch stopwatch = Stopwatch.StartNew();
        DutyCycle.Pause(TimeSpan.FromSeconds(5), 10, cts.Token); // 45 s pause, cancelled after 200 ms
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"pause took {stopwatch.Elapsed}");
    }

    [Theory]
    [Trait(GpuTestHelper.Category, GpuTestHelper.RealGpu)]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Half_Duty_Roughly_Doubles_The_Wall_Time_Of_The_Same_Search(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        // 8 characters case-sensitive: effectively never matches, so the launches are uniform.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using GpuSearchEngine engine = new(device, patterns);
        engine.RunLaunch(); // warm-up

        const int launches = 6;
        TimeSpan full = Run(engine, launches, DutyCycle.Full);
        TimeSpan half = Run(engine, launches, 50);

        // Pausing for the launch time after every launch should about double the wall time.
        // Tolerate scheduling noise: require at least 1.6x, and forbid a pathological 4x.
        double ratio = half.TotalMilliseconds / Math.Max(full.TotalMilliseconds, 1);
        Assert.True(ratio >= 1.6 && ratio <= 4.0, $"full {full.TotalMilliseconds:F0} ms, half {half.TotalMilliseconds:F0} ms, ratio {ratio:F2}");
    }

    private static TimeSpan Run(GpuSearchEngine engine, int launches, int duty)
    {
        Stopwatch total = Stopwatch.StartNew();
        for (int i = 0; i < launches; i++)
        {
            Stopwatch launch = Stopwatch.StartNew();
            engine.RunLaunch();
            DutyCycle.Pause(launch.Elapsed, duty, CancellationToken.None);
        }

        return total.Elapsed;
    }
}
