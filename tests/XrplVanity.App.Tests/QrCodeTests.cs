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
    [Fact]
    public void A_Logo_Is_Drawn_Over_The_Middle_On_Its_Own_Pad()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p
            .Add(x => x.Text, "TNA11B")
            .Add(x => x.LogoUri, "images/logo.png"));

        Assert.Equal("images/logo.png", cut.Find("svg.qr image").GetAttribute("href"));
        // A pad under it, so the mark never sits directly on the modules.
        Assert.Single(cut.FindAll("svg.qr rect.logo-pad"));
    }

    [Fact]
    public void Without_A_Logo_Uri_Nothing_Covers_The_Code()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));

        Assert.Empty(cut.FindAll("svg.qr image"));
        Assert.Empty(cut.FindAll("svg.qr rect.logo-pad"));
    }

    [Fact]
    public void A_Code_Near_Its_Capacity_Drops_The_Logo_Rather_Than_Its_Error_Correction()
    {
        // Past 1763 characters the mark costs more correction than the code can spare; the wallet
        // drops it at exactly this point too, and neither side ever lowers the level instead.
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p
            .Add(x => x.Text, new string('A', 1764))
            .Add(x => x.LogoUri, "images/logo.png"));

        Assert.Empty(cut.FindAll("svg.qr image"));
    }

    [Fact]
    public void A_Code_Just_Inside_The_Limit_Keeps_The_Logo()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p
            .Add(x => x.Text, new string('A', 1763))
            .Add(x => x.LogoUri, "images/logo.png"));

        Assert.Single(cut.FindAll("svg.qr image"));
    }
}
