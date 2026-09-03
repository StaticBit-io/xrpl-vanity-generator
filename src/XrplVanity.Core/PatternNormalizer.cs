using System.Text;

namespace XrplVanity.Core;

public readonly record struct NormalizedPattern(string Value, bool Reachable, string Original);

/// <summary>Normalizes user patterns into the XRPL base58 alphabet.</summary>
public static class PatternNormalizer
{
    /// <summary>
    /// Folds the four base58 look-alike characters onto the form that exists in the
    /// alphabet (l→L, 0→o, O→o, I→i); every other character is returned unchanged.
    /// </summary>
    public static char NormalizeChar(char c) => c switch
    {
        'l' => 'L',
        '0' => 'o',
        'O' => 'o',
        'I' => 'i',
        _ => c,
    };

    public static NormalizedPattern Normalize(string pattern)
    {
        StringBuilder sb = new(pattern.Length);
        foreach (char c in pattern)
        {
            sb.Append(NormalizeChar(c));
        }

        string value = sb.ToString();
        bool reachable = true;
        foreach (char c in value)
        {
            if (XrplBase58.Alphabet.IndexOf(c) < 0)
            {
                reachable = false;
                break;
            }
        }

        return new NormalizedPattern(value, reachable, pattern);
    }
}
