using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The QR code dialog and the code itself.</summary>
public static class QrText
{
    /// <summary>The dialog's title and its accessible name.</summary>
    public static readonly TextId Title = Id();
    public static readonly TextId CodeLabel = Id();
    public static readonly TextId FormatGroup = Id();

    /// <summary>Plural over <c>count</c>, the number of wallets; value: <c>app</c>, the wallet's name.</summary>
    public static readonly TextId FormatBackup = Id();
    public static readonly TextId FormatSeed = Id();
    public static readonly TextId SeedNeedsOneWallet = Id();

    /// <summary>Value: <c>app</c>, the wallet's name, which the dialog turns into a link.</summary>
    public static readonly TextId ScannedBySeed = Id();

    /// <summary>Value: <c>app</c>.</summary>
    public static readonly TextId ScannedByBackup = Id();
    public static readonly TextId PasswordPlaceholder = Id();
    public static readonly TextId RepeatPlaceholder = Id();
    public static readonly TextId FolderPlaceholder = Id();
    public static readonly TextId Building = Id();
    public static readonly TextId ShowCode = Id();

    /// <summary>Plural over <c>count</c>, the number of wallets.</summary>
    public static readonly TextId NotEncrypted = Id();

    /// <summary>Value: <c>app</c>.</summary>
    public static readonly TextId ScanCaption = Id();
    public static readonly TextId Change = Id();
    public static readonly TextId PasswordsDoNotMatch = Id();
    public static readonly TextId NothingToEncode = Id();

    /// <summary>Value: <c>count</c>, how many of the selected wallets fit.</summary>
    public static readonly TextId TooManyWallets = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Qr", name);
}
