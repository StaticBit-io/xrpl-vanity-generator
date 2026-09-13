using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class PatternListFormatTests
{
    [Theory]
    [InlineData("static", MatchPosition.Starts, true, "starts\tstatic")]
    [InlineData("RRRRRRRR", MatchPosition.Ends, true, "ends\tRRRRRRRR")]
    [InlineData("fast", MatchPosition.Contains, true, "contains\tfast")]
    [InlineData("ALTCONN", MatchPosition.Starts, false, "starts\tALTCONN\tAa")]
    public void A_Plain_Chip_Writes_Its_Position_Text_And_Case_Marker(string text, MatchPosition position, bool ignoreCase, string expected)
    {
        Assert.Equal(expected, PatternListFormat.Format(new PatternChip(text, position, ignoreCase)));
    }

    [Theory]
    [InlineData(true, "starts\tstatic\tends\tbit")]
    [InlineData(false, "starts\tstatic\tends\tbit\tAa")]
    public void A_Paired_Chip_Writes_Both_Halves(bool ignoreCase, string expected)
    {
        Assert.Equal(expected, PatternListFormat.Format(new PatternChip("static", MatchPosition.Pair, ignoreCase, "bit")));
    }

    [Fact]
    public void A_Line_Without_Tabs_Is_A_Legacy_Starts_Word()
    {
        Assert.True(PatternListFormat.TryParse("  static  ", defaultIgnoreCase: false, out PatternChip chip));
        Assert.Equal("static", chip.Text);
        Assert.Equal(MatchPosition.Starts, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Null(chip.EndsText);
    }

    [Theory]
    [InlineData("static", MatchPosition.Starts, true, null)]
    [InlineData("RRRRRRRR", MatchPosition.Ends, true, null)]
    [InlineData("fast", MatchPosition.Contains, false, null)]
    [InlineData("static", MatchPosition.Pair, true, "bit")]
    [InlineData("static", MatchPosition.Pair, false, "bit")]
    public void Every_Chip_Survives_A_Round_Trip(string text, MatchPosition position, bool ignoreCase, string? endsText)
    {
        PatternChip written = new(text, position, ignoreCase, endsText);

        // The default is the opposite of the chip's own flag, so a parse that ignored the written
        // marker and fell back to the default would fail this.
        Assert.True(PatternListFormat.TryParse(PatternListFormat.Format(written), !ignoreCase, out PatternChip read));

        Assert.Equal(text, read.Text);
        Assert.Equal(position, read.Position);
        Assert.Equal(ignoreCase, read.IgnoreCase);
        Assert.Equal(endsText, read.EndsText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("sideways\tstatic")]
    [InlineData("starts\t")]
    [InlineData("starts\tstatic\tXx")]
    [InlineData("starts\tstatic\tcontains\tbit")]
    [InlineData("starts\ta\tends\tb\tc\td")]
    public void A_Line_Outside_The_Grammar_Is_Rejected(string line)
    {
        Assert.False(PatternListFormat.TryParse(line, defaultIgnoreCase: true, out _));
    }
}
