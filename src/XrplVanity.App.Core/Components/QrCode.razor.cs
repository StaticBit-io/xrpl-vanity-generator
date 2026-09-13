using System.Collections;
using System.Text;
using Microsoft.AspNetCore.Components;
using QRCoder;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Draws a QR code as inline SVG. Not an &lt;img&gt; and not a PNG: an image inside a WebView offers
/// "Save image as…" from its context menu, which would put a key on disk in one click. The whole
/// symbol is one &lt;path&gt; of module-sized squares, so a large code is still a single DOM node.
/// </summary>
public partial class QrCode : ComponentBase
{
    private string _path = string.Empty;
    private int _modules = 1;

    [Parameter, EditorRequired] public string Text { get; set; } = string.Empty;

    protected override void OnParametersSet()
    {
        // Level H is what the wallet measured its capacity against. The encoding mode is left to
        // the library: the Base45 alphabet is the QR alphanumeric set, so it picks alphanumeric on
        // its own, and forcing UTF-8 would drop it to byte mode and cost a third of the capacity.
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(Text, QRCodeGenerator.ECCLevel.H);

        _modules = data.ModuleMatrix.Count;
        _path = BuildPath(data.ModuleMatrix);
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
