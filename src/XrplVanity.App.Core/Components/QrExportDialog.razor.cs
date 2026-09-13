using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Shows a selection of results as a QR code. The dialog builds nothing until it is asked to: the
/// format and the password are settled first, and only then does a secret appear on screen.
/// </summary>
public partial class QrExportDialog : ComponentBase, IDisposable
{
    /// <summary>Long enough to line a phone camera up — the 15 s seed reveal is not.</summary>
    private const int HideSeconds = 60;

    /// <summary>1234 container bytes become 1851 or 1852 Base45 characters at correction level H.</summary>
    private const int MaxCodeCharacters = 1852;

    private ElementReference _box;
    private ElementReference _close;
    private ITimer? _hideTimer;
    private string? _code;
    private string? _error;
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private bool _seedOnly;
    private bool _building;
    private bool _wasVisible;
    private volatile bool _disposed;

    /// <summary>The rows to encode. Null or empty keeps the dialog closed.</summary>
    [Parameter] public IReadOnlyList<SessionResult>? Rows { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

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
            _error = "The passwords do not match.";
            return;
        }

        if (_seedOnly)
        {
            SetCode(Rows[0].Seed);
            return;
        }

        _building = true;
        try
        {
            string? password = _password.Length == 0 ? null : _password;
            BackupWallet[] wallets = ToWallets(Rows, Rows.Count);

            // 600 000 PBKDF2 iterations is a visible pause on the render thread; the build runs off it.
            string code = await Task.Run(() => StaticBitBackup.ToQrText(wallets, password));
            SetCode(code);
        }
        catch (ArgumentException)
        {
            _error = "Nothing to encode.";
        }
        finally
        {
            _building = false;
        }
    }

    private void SetCode(string code)
    {
        if (!_seedOnly && code.Length > MaxCodeCharacters)
        {
            _error = $"Too many wallets for one code. Select at most {Fitting()} of them.";
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
            if (StaticBitBackup.Pack(ToWallets(Rows, count), password).Length <= StaticBitBackup.MaxQrContainerBytes)
            {
                return count;
            }
        }

        return 0;
    }

    private static BackupWallet[] ToWallets(IReadOnlyList<SessionResult> rows, int count)
    {
        BackupWallet[] wallets = new BackupWallet[count];
        for (int i = 0; i < count; i++)
        {
            SessionResult row = rows[i];
            wallets[i] = new BackupWallet(row.Seed, row.Address, row.Pattern);
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
        _seedOnly = false;
        _wasVisible = false;
    }

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnClose.InvokeAsync() : Task.CompletedTask;

    /// <summary>Focus lands on Close, the way the confirmation dialog focuses Cancel.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Rows is { Count: > 0 } && !_wasVisible)
        {
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
    }

    public void Dispose()
    {
        _disposed = true;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }
}
