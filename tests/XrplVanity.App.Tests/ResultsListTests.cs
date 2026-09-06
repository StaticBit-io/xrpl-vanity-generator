using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class ResultsListTests : BunitContext
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeFileDialog _dialog = new();
    private readonly FakeTimeProvider _time = new();

    private static SessionResult R(int i) => new($"rAddr{i}", $"sEdSeed{i}", "ab", "Starts", DateTimeOffset.UnixEpoch, "cpu");

    private IRenderedComponent<ResultsList> RenderList(params SessionResult[] items)
    {
        Services.AddSingleton<Core.Services.IClipboardService>(_clipboard);
        Services.AddSingleton<Core.Services.IFileDialogService>(_dialog);
        Services.AddSingleton<TimeProvider>(_time);
        return Render<ResultsList>(p => p.Add(x => x.Items, items).Add(x => x.RevealSeconds, 15).Add(x => x.ClipboardClearSeconds, 30));
    }

    [Fact]
    public void Seeds_Are_Masked_Until_Revealed_And_Hide_Again_After_The_Timeout()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim());
        Assert.DoesNotContain("sEdSeed1", cut.Markup);

        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        _time.Advance(TimeSpan.FromSeconds(16));
        cut.WaitForAssertion(() => Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim()));
    }

    [Fact]
    public void Copying_A_Seed_Uses_The_Secret_Path_With_The_Configured_Delay()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.copy-seed").Click();
        Assert.Equal("sEdSeed1", _clipboard.Text);
        Assert.Equal(TimeSpan.FromSeconds(30), _clipboard.ClearAfter);

        cut.Find("button.copy-address").Click();
        Assert.Equal("rAddr1", _clipboard.Text);
        Assert.Null(_clipboard.ClearAfter);
    }

    [Fact]
    public void Export_Writes_The_Cli_Tsv_Format()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        _dialog.PathToReturn = path;
        try
        {
            IRenderedComponent<ResultsList> cut = RenderList(R(1), R(2));
            cut.Find("button.export").Click();
            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));
            Assert.Equal("rAddr1\tsEdSeed1\nrAddr2\tsEdSeed2\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cancelled_Export_Writes_Nothing_And_Shows_No_Error()
    {
        _dialog.PathToReturn = null;
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.export").Click();
        Assert.Empty(cut.FindAll(".export-error"));
    }

    [Fact]
    public void Empty_List_Shows_The_Placeholder()
    {
        IRenderedComponent<ResultsList> cut = RenderList();
        Assert.Contains("No matches yet", cut.Markup);
    }
}
