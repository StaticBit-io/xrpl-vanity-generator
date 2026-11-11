using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Shows a selection of results as a QR code. The dialog builds nothing until it is asked to: the
/// format and the password are settled first, and only then does a secret appear on screen.
/// </summary>
public partial class QrExportDialog : IDisposable
{
    /// <summary>Long enough to line a phone camera up — the 15 s seed reveal is not.</summary>
    private const int HideSeconds = 60;

    /// <summary>1234 container bytes become 1851 or 1852 Base45 characters at correction level H.</summary>
    private const int MaxCodeCharacters = 1852;

    /// <summary>The StaticBit mark, drawn over the middle of the code when it is short enough.</summary>
    private const string LogoUri = "_content/XrplVanity.App.Core/images/staticbit-qr-logo.png";

    /// <summary>The wallet the code is for; the dialog links its name, wherever the sentence puts it.</summary>
    private const string AppName = "StaticBit";

    private const string AppUrl = "https://staticbit.io";

    private ElementReference _box;
    private ElementReference _close;
    private ITimer? _hideTimer;
    private string? _code;
    private UiText? _error;
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private string _folder = string.Empty;
    private bool _seedOnly;
    private bool _building;
    private bool _wasVisible;
    private bool _refocusClose;
    private volatile bool _disposed;

    /// <summary>The rows to encode. Null or empty keeps the dialog closed.</summary>
    [Parameter] public IReadOnlyList<SessionResult>? Rows { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// A line shown at the top of the dialog while the first-run tutorial explains it; the speech
    /// bubble stays away from this step because it would cover the dialog. Null shows nothing.
    /// </summary>
    [Parameter] public string? Note { get; set; }

    /// <summary>
    /// A bare seed is never encrypted, and a password typed for the backup format does not apply to it;
    /// a backup is unencrypted only when it was built without one.
    /// </summary>
    private bool ShowsWarning => _code is not null && (_seedOnly || _password.Length == 0);

    protected override void OnParametersSet()
    {
        if (Rows is { Count: > 0 })
        {
            return;
        }

        // Closed: nothing about the last set may survive into the next one.
        Reset();
    }

    private void SetFormat(bool seedOnly)
    {
        _seedOnly = seedOnly;
        // The two formats are different secrets; a code left on screen would belong to the old one.
        DropCode();
    }

    private async Task ShowCode()
    {
        _error = null;
        if (Rows is not { Count: > 0 }) return;

        if (!_seedOnly && _password != _passwordConfirm)
        {
            _error = UiText.Of(QrText.PasswordsDoNotMatch);
            return;
        }

        // The clicked button is replaced by the build state and then by the code: focus has to be
        // put back after each of those renders.
        _refocusClose = true;
        if (_seedOnly)
        {
            SetCode(Rows[0].Seed);
            return;
        }

        _building = true;
        try
        {
            string? password = _password.Length == 0 ? null : _password;
            BackupWallet[] wallets = ToWallets(Rows, Rows.Count, _folder);

            // 600 000 PBKDF2 iterations is a visible pause on the render thread; the build runs off it.
            string code = await Task.Run(() => StaticBitBackup.ToQrText(wallets, password));
            SetCode(code);
        }
        catch (ArgumentException)
        {
            _error = UiText.Of(QrText.NothingToEncode);
        }
        finally
        {
            _building = false;
            _refocusClose = true;
        }
    }

    private void ChangeCode()
    {
        // Change goes away with the code it was clicked to remove.
        _refocusClose = true;
        DropCode();
    }

    private void SetCode(string code)
    {
        if (!_seedOnly && code.Length > MaxCodeCharacters)
        {
            _error = UiText.Of(QrText.TooManyWallets, ("count", Fitting()));
            return;
        }

        _code = code;
        _hideTimer?.Dispose();
        _hideTimer = Clock.CreateTimer(_ => Hide(), null, TimeSpan.FromSeconds(HideSeconds), Timeout.InfiniteTimeSpan);
    }

    /// <summary>How many of the selected rows would fit, found by packing progressively smaller sets.</summary>
    private int Fitting()
    {
        if (Rows is null) return 0;

        string? password = _password.Length == 0 ? null : _password;
        for (int count = Rows.Count - 1; count >= 1; count--)
        {
            if (StaticBitBackup.Pack(ToWallets(Rows, count, _folder), password).Length <= StaticBitBackup.MaxQrContainerBytes)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>
    /// The rows as backup entries. A blank folder is left out of the payload entirely; one longer
    /// than the wallet accepts is cut rather than sent whole, because the wallet's answer to a name
    /// it cannot use is to file the wallet somewhere else without saying so.
    /// </summary>
    internal static BackupWallet[] ToWallets(IReadOnlyList<SessionResult> rows, int count, string? folder)
    {
        string? trimmed = folder?.Trim();
        if (trimmed is { Length: 0 }) trimmed = null;
        if (trimmed is { Length: > StaticBitBackup.MaxFolderNameLength })
        {
            trimmed = trimmed[..StaticBitBackup.MaxFolderNameLength];
        }

        BackupWallet[] wallets = new BackupWallet[count];
        for (int i = 0; i < count; i++)
        {
            SessionResult row = rows[i];
            wallets[i] = new BackupWallet(row.Seed, row.Address, row.Pattern, trimmed);
        }

        return wallets;
    }

    private void Hide()
    {
        if (_disposed) return;

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed) return;

                DropCode();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer is gone; nothing to refresh.
        }
        catch (InvalidOperationException)
        {
            // The component is no longer attached to a renderer.
        }
    }

    private void DropCode()
    {
        _code = null;
        _error = null;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }

    private void Reset()
    {
        DropCode();
        _password = string.Empty;
        _passwordConfirm = string.Empty;
        _folder = string.Empty;
        _seedOnly = false;
        _wasVisible = false;
        _refocusClose = false;
    }

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnClose.InvokeAsync() : Task.CompletedTask;

    /// <summary>
    /// Focus lands on Close, the way the confirmation dialog focuses Cancel. It also returns there
    /// after Show code and Change, because the button that was clicked goes away: focus on a removed
    /// element falls to the page body, outside the dialog, and Escape typed there never reaches the
    /// dialog's key handler. Nothing else moves it: a format change keeps its own button, and the
    /// timed hide can fire while the user is typing in a field.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Rows is not { Count: > 0 })
        {
            return;
        }

        bool refocus = _refocusClose;
        _refocusClose = false;
        if (_wasVisible && !refocus)
        {
            return;
        }

        _wasVisible = true;
        try
        {
            await _close.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            try
            {
                await _box.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // The dialog closed in the same batch; nothing to focus.
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }
}
