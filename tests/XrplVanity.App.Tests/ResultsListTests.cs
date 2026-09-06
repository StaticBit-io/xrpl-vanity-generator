using Bunit;
using Microsoft.AspNetCore.Components;
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

    private void RegisterServices()
    {
        Services.AddSingleton<Core.Services.IClipboardService>(_clipboard);
        Services.AddSingleton<Core.Services.IFileDialogService>(_dialog);
        Services.AddSingleton<TimeProvider>(_time);
    }

    private IRenderedComponent<ResultsList> RenderList(params SessionResult[] items)
    {
        RegisterServices();
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
    public void Export_Writes_Rows_Chronologically_Oldest_First()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        _dialog.PathToReturn = path;
        try
        {
            SessionResult older = R(1) with { FoundAt = DateTimeOffset.UnixEpoch };
            SessionResult newer = R(2) with { FoundAt = DateTimeOffset.UnixEpoch.AddMinutes(1) };
            // Items is passed newest-first, matching how the live view model exposes results.
            IRenderedComponent<ResultsList> cut = RenderList(newer, older);
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

    [Fact]
    public void Show_Target_Renders_The_Target_Column_And_Marks_The_Row()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.ShowTarget, true));

        Assert.Contains("has-target", cut.Find(".result-row").ClassName);
        Assert.Equal("cpu", cut.Find(".target").TextContent.Trim());
    }

    [Fact]
    public void Without_Show_Target_The_Row_Has_Neither_Class_Nor_Column()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.ShowTarget, false));

        Assert.DoesNotContain("has-target", cut.Find(".result-row").ClassName);
        Assert.Empty(cut.FindAll(".target"));
    }

    [Fact]
    public void On_Delete_Renders_The_Delete_Button_And_Invokes_The_Callback_With_The_Item()
    {
        RegisterServices();
        SessionResult item = R(1);
        SessionResult? deleted = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { item })
            .Add(x => x.OnDelete, EventCallback.Factory.Create<SessionResult>(this, r => deleted = r)));

        Assert.Contains("has-delete", cut.Find(".result-row").ClassName);
        cut.Find("button.delete").Click();
        Assert.Equal(item, deleted);
    }

    [Fact]
    public void Without_On_Delete_There_Is_No_Delete_Button()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p.Add(x => x.Items, new[] { R(1) }));

        Assert.DoesNotContain("has-delete", cut.Find(".result-row").ClassName);
        Assert.Empty(cut.FindAll("button.delete"));
    }

    [Fact]
    public void A_Revealed_Seed_Survives_A_Re_Render_With_A_New_List_Instance_Of_The_Same_Item()
    {
        // SearchViewModel.Results/HistoryPage.Filtered() hand back a fresh collection instance on
        // every access, even when nothing actually changed (e.g. an unrelated progress tick), so
        // ResultsList must key its per-address state off the addresses present in Items, not off
        // whether the Items reference itself changed.
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        cut.Render(p => p.Add(x => x.Items, new[] { R(1) }));

        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());
    }

    [Fact]
    public void A_Revealed_Seed_Is_Pruned_When_Its_Row_Leaves_The_List()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        cut.Render(p => p.Add(x => x.Items, Array.Empty<SessionResult>()));

        Assert.Empty(cut.FindAll(".seed"));
        Assert.Contains("No matches yet", cut.Markup);
    }

    [Fact]
    public void Disposing_After_Reveal_Does_Not_Throw_When_The_Timer_Fires()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();

        cut.Instance.Dispose();

        _time.Advance(TimeSpan.FromSeconds(16));
    }
}
