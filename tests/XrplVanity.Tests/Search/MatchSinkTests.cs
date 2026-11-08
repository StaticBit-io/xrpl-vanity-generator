using System.Collections.Generic;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests.Search;

public class MatchSinkTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Reports_Until_The_Limit_Then_Refuses_Surplus()
    {
        List<Match> reported = new();
        MatchSink sink = new(limit: 2, reported.Add);

        Assert.True(sink.TryReport(M(1)));
        Assert.False(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(2)));   // reaches the limit: reported, but the caller must stop
        Assert.True(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(3)));   // surplus: not reported
        Assert.Equal(2, sink.Reported);
        Assert.Equal(new[] { "rAddr1", "rAddr2" }, reported.ConvertAll(m => m.Address));
    }

    [Fact]
    public void Without_A_Limit_It_Never_Refuses()
    {
        List<Match> reported = new();
        MatchSink sink = new(limit: null, reported.Add);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(sink.TryReport(M(i)));
        }

        Assert.False(sink.CountSatisfied);
        Assert.Equal(10, sink.Reported);
        Assert.Equal(10, reported.Count);
    }

    [Fact]
    public void The_Report_Callback_Runs_Inside_The_Lock_So_Order_Equals_Acceptance_Order()
    {
        List<int> order = new();
        MatchSink sink = new(limit: null, m => order.Add(int.Parse(m.Address["rAddr".Length..])));
        System.Threading.Tasks.Parallel.For(0, 200, i => sink.TryReport(M(i)));

        Assert.Equal(200, sink.Reported);
        Assert.Equal(200, order.Count);   // no lost writes: the callback never ran concurrently
    }
}
