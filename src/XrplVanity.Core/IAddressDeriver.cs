using System;

namespace XrplVanity.Core;

/// <summary>Derives a classic XRPL address from 16 bytes of entropy.</summary>
public interface IAddressDeriver
{
    /// <summary>Writes the address into <paramref name="dest"/> and returns its length.</summary>
    int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest);
}
