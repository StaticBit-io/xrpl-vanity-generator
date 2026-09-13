using System.Text;

namespace XrplVanity.Core;

/// <summary>
/// RFC 9285 Base45. The StaticBit wallet's backup QR is text, not bytes — both mobile scanners
/// hand a string back and anything above 0x7F does not survive that — and this alphabet is exactly
/// the QR alphanumeric character set, which the encoder packs at 5.5 bits per character instead of
/// 8 bits per byte.
/// </summary>
public static class Base45
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    private const int Radix = 45;
    private const int RadixSquared = Radix * Radix;

    /// <summary>A pair of bytes becomes three characters, least significant digit first; a lone trailing byte becomes two.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        StringBuilder builder = new(((data.Length + 1) / 2) * 3);

        int index = 0;
        while (index + 1 < data.Length)
        {
            int value = (data[index] << 8) | data[index + 1];
            builder.Append(Alphabet[value % Radix]);
            builder.Append(Alphabet[(value / Radix) % Radix]);
            builder.Append(Alphabet[value / RadixSquared]);
            index += 2;
        }

        if (index < data.Length)
        {
            int value = data[index];
            builder.Append(Alphabet[value % Radix]);
            builder.Append(Alphabet[value / Radix]);
        }

        return builder.ToString();
    }
}
