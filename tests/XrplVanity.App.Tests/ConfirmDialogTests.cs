using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using Xunit;

namespace XrplVanity.App.Tests;

public class ConfirmDialogTests : BunitContext
{
    [Fact]
    public void Hidden_Dialog_Renders_Nothing()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p.Add(x => x.Visible, false));
        Assert.Empty(cut.FindAll(".modal-box"));
    }

    [Fact]
    public void Shows_The_Title_Message_And_Confirm_Label()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "Delete 3 results?")
            .Add(x => x.Message, "Their seeds are deleted with them.")
            .Add(x => x.ConfirmLabel, "Delete 3"));

        Assert.Contains("Delete 3 results?", cut.Find(".modal-box h3").TextContent);
        Assert.Contains("Their seeds are deleted with them.", cut.Find(".modal-box p").TextContent);
        Assert.Equal("Delete 3", cut.Find("button.confirm").TextContent.Trim());
    }

    [Fact]
    public void Lists_The_Addresses_And_Truncates_Past_Ten()
    {
        string[] addresses = Enumerable.Range(0, 24).Select(i => $"rAddress{i}").ToArray();
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Addresses, addresses));

        string listed = cut.Find(".what").TextContent;
        Assert.Contains("rAddress0", listed);
        Assert.Contains("rAddress9", listed);
        Assert.DoesNotContain("rAddress10", listed);
        Assert.Contains("and 14 more", listed);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(11, "and 1 more")]
    [InlineData(24, "and 14 more")]
    public void The_More_Line_Appears_Only_Past_The_Cap(int count, string? expected)
    {
        string[] addresses = Enumerable.Range(0, count).Select(i => $"rAddress{i}").ToArray();
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Addresses, addresses));

        string listed = cut.Find(".what").TextContent;
        if (expected is null)
        {
            Assert.DoesNotContain("more", listed);
        }
        else
        {
            Assert.Contains(expected, listed);
        }
    }

    [Fact]
    public void Cancel_And_The_More_Line_Follow_The_Language()
    {
        TextCatalog german = TextCatalog.Transformed(UiLanguage.German, phrase => "[de] " + phrase);
        string[] addresses = Enumerable.Range(0, 12).Select(i => $"rAddress{i}").ToArray();

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .AddCascadingValue(german)
            .Add(x => x.Visible, true)
            .Add(x => x.Addresses, addresses));

        Assert.Equal("[de] Cancel", cut.Find("button.cancel").TextContent.Trim());
        Assert.Equal("[de] Delete", cut.Find("button.confirm").TextContent.Trim());
        Assert.Equal("[de] …and 2 more", cut.Find(".more").TextContent.Trim());
    }

    [Fact]
    public void Without_Addresses_There_Is_No_List()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p.Add(x => x.Visible, true));
        Assert.Empty(cut.FindAll(".what"));
    }

    [Fact]
    public void Confirm_And_Cancel_Raise_Their_Callbacks()
    {
        int confirms = 0;
        int cancels = 0;
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnConfirm, () => confirms++)
            .Add(x => x.OnCancel, () => cancels++));

        cut.Find("button.confirm").Click();
        cut.Find("button.cancel").Click();

        Assert.Equal(1, confirms);
        Assert.Equal(1, cancels);
    }

    [Fact]
    public void Escape_Cancels_And_Other_Keys_Do_Not()
    {
        int cancels = 0;
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnCancel, () => cancels++));

        cut.Find(".modal-box").KeyDown("a");
        Assert.Equal(0, cancels);

        cut.Find(".modal-box").KeyDown("Escape");
        Assert.Equal(1, cancels);
    }
}
