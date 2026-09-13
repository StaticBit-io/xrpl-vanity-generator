using System.Text;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public static class PatternListFile
{
    /// <summary>Writes one <see cref="PatternListFormat"/> line per chip through a temp file so a failure leaves nothing partial.</summary>
    public static void Write(string path, IEnumerable<PatternChip> chips)
    {
        string temp = path + ".tmp";
        StringBuilder sb = new();
        foreach (PatternChip chip in chips)
        {
            sb.Append(PatternListFormat.Format(chip)).Append('\n');
        }

        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
