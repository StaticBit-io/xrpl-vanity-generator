using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class QrExportDialogTests : BunitContext
{
    private readonly FakeTimeProvider _time = new();

    public QrExportDialogTests()
    {
        // Registered once: bUnit refuses new services after the first render in the context.
        Services.AddSingleton<TimeProvider>(_time);
    }

    /// <summary>
    /// Deterministic but unalike: rows that differ only in a trailing digit compress so well that
    /// forty of them fit one code, and the capacity refusal would never be reached.
    /// </summary>
    private static SessionResult R(int i) => new(
        "r" + Base58Ish(33, i), "sEd" + Base58Ish(26, i + 1000),
        "static", "Starts", DateTimeOffset.UnixEpoch, "cpu");

    private static string Base58Ish(int length, int seed)
    {
        const string Alphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";
        Random random = new(seed);
        char[] chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = Alphabet[random.Next(Alphabet.Length)];
        return new string(chars);
    }

    private IRenderedComponent<QrExportDialog> Open(params SessionResult[] rows)
        => Render<QrExportDialog>(p => p.Add(x => x.Rows, rows));

    [Fact]
    public void Nothing_Secret_Is_Drawn_Until_The_Code_Is_Asked_For()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        Assert.Empty(cut.FindAll("svg.qr"));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
    }

    [Fact]
    public void Note_Is_Shown_Only_When_Set()
    {
        IRenderedComponent<QrExportDialog> plain = Open(R(1));
        Assert.Empty(plain.FindAll(".tutorial-note"));

        IRenderedComponent<QrExportDialog> noted = Render<QrExportDialog>(p => p
            .Add(x => x.Rows, new[] { R(1) })
            .Add(x => x.Note, "Close the dialog to continue."));

        Assert.Equal("Close the dialog to continue.", noted.Find(".tutorial-note").TextContent);
    }

    [Fact]
    public void Focus_Returns_To_Close_When_The_Clicked_Button_Goes_Away()
    {
        const string FocusIdentifier = "Blazor._internal.domWrapper.focus";
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.WaitForAssertion(() => Assert.Equal(1, JSInterop.Invocations.Count(i => i.Identifier == FocusIdentifier)));

        // Show code is replaced by the code; focus left on a removed button falls to the page body,
        // where Escape never reaches the dialog.
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
        cut.WaitForAssertion(() => Assert.True(JSInterop.Invocations.Count(i => i.Identifier == FocusIdentifier) >= 2));
    }

    [Fact]
    public void Focus_Moves_Only_When_Show_Code_Or_Change_Takes_The_Clicked_Button_Away()
    {
        const string FocusIdentifier = "Blazor._internal.domWrapper.focus";
        int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier == FocusIdentifier);

        // The build renders twice (button disabled, then the code), so the count is read once it stops moving.
        int Settled()
        {
            int last = FocusCalls();
            for (int quiet = 0; quiet < 4; quiet++)
            {
                Thread.Sleep(25);
                int now = FocusCalls();
                quiet = now == last ? quiet : -1;
                last = now;
            }

            return last;
        }

        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.WaitForAssertion(() => Assert.Equal(1, FocusCalls()));

        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
        int afterShow = Settled();
        Assert.True(afterShow >= 2, "Show code must put focus back on Close");

        // The seed format keeps its own button under the pointer; a format change moves nothing.
        cut.Find("button.format-seed").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("svg.qr")));
        Assert.Equal(afterShow, Settled());

        cut.Find("button.format-backup").Click();
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
        int afterSecondShow = Settled();
        Assert.True(afterSecondShow > afterShow);

        // The 60 second hide fires while the user may be typing or reading elsewhere.
        _time.Advance(TimeSpan.FromSeconds(60));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("svg.qr")));
        Assert.Equal(afterSecondShow, Settled());

        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
        int afterThirdShow = Settled();
        cut.Find("button.change-code").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("svg.qr")));
        Assert.True(Settled() > afterThirdShow, "Change must put focus back on Close");
    }

    [Fact]
    public void The_Note_Is_Tied_To_The_Dialog_As_Its_Description()
    {
        IRenderedComponent<QrExportDialog> noted = Render<QrExportDialog>(p => p
            .Add(x => x.Rows, new[] { R(1) })
            .Add(x => x.Note, "Close the dialog to continue."));

        string? describedBy = noted.Find(".qr-box").GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));
        Assert.Equal(describedBy, noted.Find(".tutorial-note").Id);

        IRenderedComponent<QrExportDialog> plain = Open(R(1));
        Assert.Null(plain.Find(".qr-box").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void One_Wallet_Offers_Both_Formats_And_Several_Offer_Only_The_Backup()
    {
        Assert.False(Open(R(1)).Find("button.format-seed").HasAttribute("disabled"));

        IRenderedComponent<QrExportDialog> many = Open(R(1), R(2));
        Assert.True(many.Find("button.format-seed").HasAttribute("disabled"));
        Assert.Contains("one wallet", many.Find(".format-note").TextContent);
    }

    [Fact]
    public void The_Seed_Format_Says_StaticBit_Can_Scan_It()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        cut.Find("button.format-seed").Click();

        Assert.Contains("Scanned by StaticBit", cut.Find(".format-note").TextContent);
        Assert.DoesNotContain("does not import", cut.Find(".format-note").TextContent);
    }

    [Fact]
    public void Every_Line_About_StaticBit_Links_To_Its_Site()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        Assert.NotEmpty(cut.FindAll(".format-note a[href='https://staticbit.io']"));

        cut.Find("button.format-seed").Click();
        Assert.NotEmpty(cut.FindAll(".format-note a[href='https://staticbit.io']"));

        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-caption a[href='https://staticbit.io']")));
    }

    [Fact]
    public void The_Backup_Code_Carries_The_Prefix_The_Wallet_Looks_For()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() =>
            Assert.StartsWith("TNA11B", cut.FindComponent<QrCode>().Instance.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void The_Seed_Format_Encodes_The_Seed_And_Nothing_Around_It()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.format-seed").Click();

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Equal(R(1).Seed, cut.FindComponent<QrCode>().Instance.Text));
    }

    [Fact]
    public void Switching_The_Format_Takes_The_Rendered_Code_Away()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));

        cut.Find("button.format-seed").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("svg.qr"));
            // A bare seed has no password and no folder, so those fields go.
            Assert.Contains("hidden", cut.Find(".backup-fields").GetAttribute("class"));
        });
    }

    [Fact]
    public void A_Code_Without_A_Password_Says_What_That_Costs()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Contains("not encrypted", cut.Find(".qr-warning").TextContent));
    }

    [Fact]
    public void A_Bare_Seed_Warns_That_It_Is_Not_Encrypted()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.format-seed").Click();

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("hidden", cut.Find(".qr-warning").GetAttribute("class"));
            Assert.Contains("this wallet", cut.Find(".qr-warning").TextContent);
        });
    }

    [Fact]
    public void A_Password_Typed_For_The_Backup_Does_Not_Silence_The_Seed_Warning()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.password").Input("secret");
        cut.Find("input.password-confirm").Input("secret");
        cut.Find("button.format-seed").Click();

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("hidden", cut.Find(".qr-warning").GetAttribute("class")));
    }

    [Fact]
    public void A_Backup_With_A_Password_Shows_No_Warning()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.password").Input("secret");
        cut.Find("input.password-confirm").Input("secret");

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("svg.qr"));
            Assert.Contains("hidden", cut.Find(".qr-warning").GetAttribute("class"));
        });
    }

    [Fact]
    public void Several_Wallets_Are_Named_In_The_Plural()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1), R(2));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Contains("these wallets", cut.Find(".qr-warning").TextContent));
    }

    [Fact]
    public void Passwords_That_Disagree_Build_Nothing()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.password").Input("one");
        cut.Find("input.password-confirm").Input("other");

        cut.Find("button.show-code").Click();

        Assert.Empty(cut.FindAll("svg.qr"));
        Assert.Contains("do not match", cut.Find(".qr-error").TextContent);
    }

    [Fact]
    public void More_Wallets_Than_One_Code_Holds_Are_Refused_With_A_Number()
    {
        SessionResult[] rows = Enumerable.Range(1, 40).Select(R).ToArray();
        IRenderedComponent<QrExportDialog> cut = Open(rows);

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Contains("Too many wallets for one code", cut.Find(".qr-error").TextContent));
        Assert.Empty(cut.FindAll("svg.qr"));
    }

    [Fact]
    public void The_Code_Hides_Itself_And_Can_Be_Brought_Back()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));

        _time.Advance(TimeSpan.FromSeconds(60));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("svg.qr")));
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
    }

    [Fact]
    public void Both_Store_Links_Are_Under_The_Code()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        Assert.Equal(
            "https://apps.apple.com/us/app/staticbit/id6745529426",
            cut.Find("a.store-ios").GetAttribute("href"));
        Assert.Equal(
            "https://play.google.com/store/apps/details?id=io.staticbit.wallet",
            cut.Find("a.store-android").GetAttribute("href"));
    }

    [Fact]
    public void A_Closed_Dialog_Renders_Nothing_At_All()
    {
        IRenderedComponent<QrExportDialog> cut = Render<QrExportDialog>(
            p => p.Add(x => x.Rows, (IReadOnlyList<SessionResult>?)null));

        Assert.Empty(cut.FindAll(".qr-box"));
    }
    [Fact]
    public void A_Folder_Name_Is_Carried_On_Every_Wallet_In_The_Code()
    {
        BackupWallet[] wallets = QrExportDialog.ToWallets(new[] { R(1), R(2) }, 2, "Vanity");

        Assert.All(wallets, w => Assert.Equal("Vanity", w.Folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void No_Folder_Name_Means_No_Folder_Field(string? folder)
    {
        Assert.Null(Assert.Single(QrExportDialog.ToWallets(new[] { R(1) }, 1, folder)).Folder);
    }

    [Fact]
    public void A_Folder_Name_Too_Long_For_The_Wallet_Is_Cut_Rather_Than_Silently_Dropped()
    {
        // The wallet ignores a name past 64 characters and files the wallet in the default folder
        // instead — cutting it here at least puts it where the user was aiming.
        BackupWallet one = Assert.Single(QrExportDialog.ToWallets(new[] { R(1) }, 1, new string('x', 70)));

        Assert.Equal(64, one.Folder!.Length);
    }

    [Fact]
    public void The_Backup_Fields_Belong_To_The_Backup_Format_Alone()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        Assert.DoesNotContain("hidden", cut.Find(".backup-fields").GetAttribute("class"));

        cut.Find("button.format-seed").Click();

        Assert.Contains("hidden", cut.Find(".backup-fields").GetAttribute("class"));
        // The folder field belongs to the container, which a bare seed does not have.
        Assert.Single(cut.FindAll("input.folder"));
    }

    [Fact]
    public void The_Code_Carries_The_StaticBit_Mark()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("staticbit-qr-logo.png", cut.FindComponent<QrCode>().Instance.LogoUri));
    }
    [Fact]
    public void A_Code_On_Screen_Puts_The_Form_Away()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        Assert.DoesNotContain("hidden", cut.Find(".backup-fields").GetAttribute("class"));
        Assert.Empty(cut.FindAll("button.change-code"));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() =>
        {
            // The fields no longer decide anything: the code is already built from them.
            Assert.Contains("hidden", cut.Find(".backup-fields").GetAttribute("class"));
            Assert.Single(cut.FindAll("button.change-code"));
        });
    }

    [Fact]
    public void Change_Brings_The_Form_Back_With_What_Was_Typed_Still_In_It()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.password").Input("one");
        cut.Find("input.password-confirm").Input("one");
        cut.Find("input.folder").Input("Vanity");
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));

        cut.Find("button.change-code").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("svg.qr"));
            Assert.DoesNotContain("hidden", cut.Find(".backup-fields").GetAttribute("class"));
            Assert.Equal("Vanity", cut.Find("input.folder").GetAttribute("value"));
            Assert.Single(cut.FindAll("button.show-code"));
        });
    }

    [Fact]
    public void A_Changed_Password_Builds_A_Different_Code()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("svg.qr")));
        string first = cut.FindComponent<QrCode>().Instance.Text;

        cut.Find("button.change-code").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-code")));
        cut.Find("input.password").Input("correct horse");
        cut.Find("input.password-confirm").Input("correct horse");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.show-code")));
        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.NotEqual(first, cut.FindComponent<QrCode>().Instance.Text));
    }
}
