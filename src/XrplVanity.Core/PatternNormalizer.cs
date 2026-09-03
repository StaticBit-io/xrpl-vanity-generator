using System.Text;

namespace XrplVanity.Core;

public readonly record struct NormalizedPattern(string Value, bool Reachable, string Original);

/// <summary>Normalizes user patterns into the XRPL base58 alphabet.</summary>
public static class PatternNormalizer
{
    public static NormalizedPattern Normalize(string pattern)
    {
        StringBuilder sb = new(pattern.Length);
        foreach (char c in pattern)
        {
            char mapped = c switch
            {
                'l' => 'L',
                '0' => 'o',
                'O' => 'o',
                'I' => 'i',
                _ => c,
            };
            sb.Append(mapped);
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
