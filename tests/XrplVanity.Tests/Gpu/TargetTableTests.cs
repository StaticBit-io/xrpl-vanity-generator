using System.Text;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class TargetTableTests
{
    [Fact]
    public void Groups_Are_Sorted_By_Position_And_Length_With_Ordinal_Sorted_Targets()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "q", ignoreCase: false)
            .Add(MatchPosition.Ends, "z", ignoreCase: true)
            .Add(MatchPosition.Starts, "ab", ignoreCase: true)
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(3, table.GroupCount);
        // group 0: starts, length 2, 4 targets sorted ordinal: AB Ab aB ab
        Assert.Equal(new uint[] { TargetTable.PositionStarts, 2, 0, 4 }, table.Groups[..4]);
        // group 1: ends, length 1, 2 targets: Z z
        Assert.Equal(new uint[] { TargetTable.PositionEnds, 1, 8, 2 }, table.Groups[4..8]);
        // group 2: contains, length 1, 1 target: q
        Assert.Equal(new uint[] { TargetTable.PositionContains, 1, 10, 1 }, table.Groups[8..12]);
        Assert.Equal("ABAbaBabZzq", Encoding.ASCII.GetString(table.Chars));
    }

    [Fact]
    public void Duplicate_Targets_Are_Stored_Once()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "ab", ignoreCase: false)
            .Add(MatchPosition.Contains, "ab", ignoreCase: false)
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(1, table.GroupCount);
        Assert.Equal(1u, table.Groups[3]);
        Assert.Equal("ab", Encoding.ASCII.GetString(table.Chars));
    }

    [Fact]
    public void Empty_Set_Has_No_Groups()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "bit@coin", ignoreCase: true) // unreachable, dropped
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(0, table.GroupCount);
        Assert.Empty(table.Chars);
    }

    [Fact]
    public void Empty_Pattern_Produces_No_Zero_Length_Group()
    {
        PatternSet set = new PatternSet.Builder().Add(MatchPosition.Starts, "", ignoreCase: true).Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(0, table.GroupCount);
    }
}
