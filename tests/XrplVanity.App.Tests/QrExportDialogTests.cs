using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
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
    public void One_Wallet_Offers_Both_Formats_And_Several_Offer_Only_The_Backup()
    {
        Assert.False(Open(R(1)).Find("button.format-seed").HasAttribute("disabled"));

        IRenderedComponent<QrExportDialog> many = Open(R(1), R(2));
        Assert.True(many.Find("button.format-seed").HasAttribute("disabled"));
        Assert.Contains("one wallet", many.Find(".format-note").TextContent);
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
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input.password")));   // the format really changed
        Assert.Empty(cut.FindAll("svg.qr"));
    }

    [Fact]
    public void A_Code_Without_A_Password_Says_What_That_Costs()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        cut.Find("button.show-code").Click();

        cut.WaitForAssertion(() => Assert.Contains("not encrypted", cut.Find(".qr-warning").TextContent));
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
}
