using System;

namespace XrplVanity.Core;

/// <summary>Base58 encode/decode over the XRPL (Ripple) alphabet.</summary>
public static class XrplBase58
{
    public const string Alphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";

    public static int Encode(ReadOnlySpan<byte> input, Span<char> dest)
    {
        Span<byte> tmp = stackalloc byte[64];
        input.CopyTo(tmp);
        int len = input.Length;

        int zeros = 0;
        while (zeros < len && tmp[zeros] == 0)
        {
            zeros++;
        }

        Span<char> rev = stackalloc char[128];
        int revLen = 0;
        int start = zeros;
        while (start < len)
        {
            int rem = 0;
            for (int i = start; i < len; i++)
            {
                int cur = (rem << 8) | tmp[i];
                tmp[i] = (byte)(cur / 58);
                rem = cur % 58;
            }

            rev[revLen++] = Alphabet[rem];
            while (start < len && tmp[start] == 0)
            {
                start++;
            }
        }

        int pos = 0;
        for (int i = 0; i < zeros; i++)
        {
            dest[pos++] = Alphabet[0];
        }

        for (int i = revLen - 1; i >= 0; i--)
        {
            dest[pos++] = rev[i];
        }

        return pos;
    }

    public static byte[] Decode(string text)
    {
        Span<byte> acc = stackalloc byte[128];
        int accLen = 1;
        foreach (char c in text)
        {
            int digit = Alphabet.IndexOf(c);
            if (digit < 0)
            {
                throw new FormatException($"Invalid base58 character '{c}'.");
            }

            int carry = digit;
            for (int i = 0; i < accLen; i++)
            {
                int cur = acc[i] * 58 + carry;
                acc[i] = (byte)(cur & 0xFF);
                carry = cur >> 8;
            }

            while (carry > 0)
            {
                acc[accLen++] = (byte)(carry & 0xFF);
                carry >>= 8;
            }
        }

        int zeros = 0;
        while (zeros < text.Length && text[zeros] == Alphabet[0])
        {
            zeros++;
        }

        byte[] result = new byte[zeros + accLen];
        for (int i = 0; i < accLen; i++)
        {
            result[zeros + i] = acc[accLen - 1 - i];
        }

        return result;
    }
}
