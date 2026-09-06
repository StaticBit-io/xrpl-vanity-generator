using System.Text;

namespace XrplVanity.App.Core.Components;

public static class TsvExport
{
    /// <summary>Writes address&lt;TAB&gt;seed lines (the CLI's --out format) through a temp file so a failure leaves nothing partial.</summary>
    public static void Write(string path, IEnumerable<(string Address, string Seed)> rows)
    {
        string temp = path + ".tmp";
        StringBuilder sb = new();
        foreach ((string address, string seed) in rows)
        {
            sb.Append(address).Append('\t').Append(seed).Append('\n');
        }

        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
