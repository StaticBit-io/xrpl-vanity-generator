using Bunit;
using XrplVanity.App.Core.Components;
using Xunit;

namespace XrplVanity.App.Tests;

public class QrCodeTests : BunitContext
{
    [Fact]
    public void The_Code_Is_Inline_Svg_And_Never_An_Image()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));

        Assert.Single(cut.FindAll("svg.qr"));
        // An <img> is what makes "Save image as…" appear in WebView2; an inline SVG offers it nothing.
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void The_View_Box_Is_Square_And_Counts_Modules_Not_Pixels()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));

        string[] box = cut.Find("svg.qr").GetAttribute("viewBox")!.Split(' ');
        Assert.Equal("0", box[0]);
        Assert.Equal("0", box[1]);
        Assert.Equal(box[2], box[3]);
        Assert.True(int.Parse(box[2]) >= 21);   // version 1 is 21 modules across
    }

    [Fact]
    public void Longer_Text_Needs_A_Bigger_Symbol()
    {
        IRenderedComponent<QrCode> small = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));
        IRenderedComponent<QrCode> large = Render<QrCode>(p => p.Add(x => x.Text, new string('A', 600)));

        Assert.True(Modules(large) > Modules(small));

        static int Modules(IRenderedComponent<QrCode> cut) =>
            int.Parse(cut.Find("svg.qr").GetAttribute("viewBox")!.Split(' ')[2]);
    }

    [Fact]
    public void Changing_The_Text_Redraws_The_Code()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));
        string before = cut.Find("svg.qr path").GetAttribute("d")!;

        cut.Render(p => p.Add(x => x.Text, "TNA11BW50"));

        Assert.NotEqual(before, cut.Find("svg.qr path").GetAttribute("d"));
    }
}
