using System.Collections;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using QRCoder;
using XrplVanity.App.Core.Localization;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Draws a QR code as inline SVG. Not an &lt;img&gt; and not a PNG: an image inside a WebView offers
/// "Save image as…" from its context menu, which would put a key on disk in one click. The whole
/// symbol is one &lt;path&gt; of module-sized squares, so a large code is still a single DOM node.
/// </summary>
public partial class QrCode
{
    /// <summary>
    /// Past this many characters the mark is dropped instead of drawn. A logo is damage the error
    /// correction has to absorb, and a code this long has already spent most of what level H gives
    /// it. The wallet draws the line at the same place, and neither side ever lowers the level to
    /// make a logo fit.
    /// </summary>
    private const int MaxLengthWithLogo = 1763;

    /// <summary>How much of the code's width the mark covers, and how much wider its pad is.</summary>
    private const double LogoShare = 0.20;
    private const double PadShare = 0.26;

    private string _path = string.Empty;
    private int _modules = 1;
    private LogoBox? _logo;

    [Parameter, EditorRequired] public string Text { get; set; } = string.Empty;

    /// <summary>Drawn over the middle of the code when the code is short enough to spare it.</summary>
    [Parameter] public string? LogoUri { get; set; }

    protected override void OnParametersSet()
    {
        // Level H is what the wallet measured its capacity against. The encoding mode is left to
        // the library: the Base45 alphabet is the QR alphanumeric set, so it picks alphanumeric on
        // its own, and forcing UTF-8 would drop it to byte mode and cost a third of the capacity.
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(Text, QRCodeGenerator.ECCLevel.H);

        _modules = data.ModuleMatrix.Count;
        _path = BuildPath(data.ModuleMatrix);
        _logo = LogoUri is null || Text.Length > MaxLengthWithLogo ? null : LogoBox.Centred(_modules);
    }

    /// <summary>The mark and its pad, in module units, so the SVG needs no arithmetic of its own.</summary>
    private sealed record LogoBox(string X, string Size, string PadX, string PadSize)
    {
        public static LogoBox Centred(int modules)
        {
            double size = modules * LogoShare;
            double pad = modules * PadShare;
            return new LogoBox(
                Round((modules - size) / 2),
                Round(size),
                Round((modules - pad) / 2),
                Round(pad));
        }

        private static string Round(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string BuildPath(List<BitArray> matrix)
    {
        StringBuilder path = new();
        for (int y = 0; y < matrix.Count; y++)
        {
            BitArray row = matrix[y];
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x]) path.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
            }
        }

        return path.ToString();
    }
}
