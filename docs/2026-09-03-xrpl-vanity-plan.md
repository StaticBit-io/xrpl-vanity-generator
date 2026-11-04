# XRPL Vanity Generator (.NET) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone .NET 10 CLI that generates XRP Ledger Ed25519 vanity addresses and doubles as the correctness reference for a later GPU kernel.

**Architecture:** A pure `XrplVanity.Core` library holds the two functions a GPU kernel must later reproduce — `entropy -> address` and `address -> match?` — with no threading and no I/O. `XrplVanity.Cli` adds argument parsing, worker threads and output. `XrplVanity.Tests` (xUnit v3) pins correctness against a known vector and cross-checks every derived address against the independent `Xrpl.Keypairs` SDK.

**Tech Stack:** .NET 10, C# latest, BouncyCastle.Cryptography 2.7.0 (managed Ed25519/RIPEMD-160), System.Security.Cryptography (SHA-256/512, CSPRNG), xUnit v3 4.0.0.

**Spec:** `docs/2026-09-03-xrpl-vanity-design.md`

## Global Constraints

- Target framework: `net10.0`. `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`.
- Crypto: `BouncyCastle.Cryptography` `2.7.0` for Ed25519 public-key derivation and RIPEMD-160; `System.Security.Cryptography` for SHA-256/SHA-512 and `RandomNumberGenerator`. No libsodium/native dependency in v1.
- Ed25519 only. No secp256k1, no regex, no GPU in this plan.
- Tests: `xunit.v3` `4.0.0` with the built-in `Assert`. **No FluentAssertions.**
- **Run tests with `dotnet run`, never `dotnet test`** — `dotnet test` does not work with xunit.v3 on net10. Filter by class: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*ClassName"`. Run the whole suite by omitting the filter.
- All repository artifacts (code, comments, docs, commit messages) in English.
- Repository stays local. Do not publish, push, or open a PR unless the user explicitly commands it. Commit locally after each task.
- XRPL base58 alphabet (constant, used verbatim): `rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz`.
- Known correctness vector: seed `sEdTuRgwYiEcubPJfRJUArVyn7AXkVj` derives address `rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK`.

## File Structure

```
xrpl-vanity/
  XrplVanity.sln
  src/XrplVanity.Core/
    XrplVanity.Core.csproj
    XrplBase58.cs          # base58 encode/decode over the XRPL alphabet
    Seed.cs                # sEd... family seed encode/decode (entropy <-> string)
    IAddressDeriver.cs     # entropy16 -> classic address (interface)
    BouncyCastleDeriver.cs # managed Ed25519 implementation (one instance per thread)
    MatchPosition.cs       # enum: Starts, Ends, Contains
    PatternNormalizer.cs   # normalize a pattern into the base58 alphabet; reachability
    PatternSet.cs          # bucketed matcher across positions
    Match.cs               # result record: address, seed, pattern, position
    CandidateEvaluator.cs  # entropy -> Match? (the GPU-shared semantics)
  src/XrplVanity.Cli/
    XrplVanity.Cli.csproj
    CliOptions.cs          # argument parsing
    Program.cs             # worker threads, RNG batches, warning, output
  tests/XrplVanity.Tests/
    XrplVanity.Tests.csproj
    XrplBase58Tests.cs
    AddressDeriverTests.cs # known vector + seed round-trip
    CrossCheckTests.cs     # vs Xrpl.Keypairs over random seeds
    PatternNormalizerTests.cs
    PatternSetTests.cs
    CandidateEvaluatorTests.cs
    CliOptionsTests.cs
  docs/
```

---

### Task 1: Scaffolding and XRPL base58 codec

**Files:**
- Create: `XrplVanity.sln`, `src/XrplVanity.Core/XrplVanity.Core.csproj`, `tests/XrplVanity.Tests/XrplVanity.Tests.csproj`
- Create: `src/XrplVanity.Core/XrplBase58.cs`
- Test: `tests/XrplVanity.Tests/XrplBase58Tests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `XrplVanity.Core.XrplBase58.Encode(ReadOnlySpan<byte> input, Span<char> dest) -> int` (writes chars, returns length; `dest` must be >= 2*input length).
  - `XrplVanity.Core.XrplBase58.Decode(string text) -> byte[]`.

- [ ] **Step 1: Create the projects and solution**

```bash
cd xrpl-vanity
dotnet new classlib -n XrplVanity.Core -o src/XrplVanity.Core -f net10.0
dotnet new xunit3 -n XrplVanity.Tests -o tests/XrplVanity.Tests -f net10.0
dotnet new sln -n XrplVanity
dotnet sln add src/XrplVanity.Core/XrplVanity.Core.csproj tests/XrplVanity.Tests/XrplVanity.Tests.csproj
dotnet add tests/XrplVanity.Tests/XrplVanity.Tests.csproj reference src/XrplVanity.Core/XrplVanity.Core.csproj
rm -f src/XrplVanity.Core/Class1.cs tests/XrplVanity.Tests/UnitTest1.cs
```

Then set both csproj `PropertyGroup` to include `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>`, and add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to Core. Pin the test package: in `XrplVanity.Tests.csproj` ensure `<PackageReference Include="xunit.v3" Version="4.0.0" />`.

If `dotnet new xunit3` is unavailable, create the test project as `dotnet new console` with `<OutputType>Exe</OutputType>` and add `<PackageReference Include="xunit.v3" Version="4.0.0" />`.

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.Tests/XrplBase58Tests.cs`:

```csharp
using System;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class XrplBase58Tests
{
    [Fact]
    public void Encode_Then_Decode_Roundtrips()
    {
        byte[] input = { 0x00, 0x01, 0x02, 0xED, 0xFF, 0x10, 0x20, 0x30, 0x40, 0x50 };
        Span<char> buffer = stackalloc char[64];
        int len = XrplBase58.Encode(input, buffer);
        string encoded = new string(buffer[..len]);

        byte[] decoded = XrplBase58.Decode(encoded);

        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Uses_Xrpl_Alphabet_First_Char_For_Leading_Zero()
    {
        byte[] input = { 0x00, 0x00, 0x05 };
        Span<char> buffer = stackalloc char[16];
        int len = XrplBase58.Encode(input, buffer);
        string encoded = new string(buffer[..len]);

        Assert.StartsWith("rr", encoded); // 'r' is alphabet[0]; two leading zero bytes -> "rr"
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*XrplBase58Tests"`
Expected: build error / FAIL — `XrplBase58` does not exist.

- [ ] **Step 4: Write minimal implementation**

`src/XrplVanity.Core/XrplBase58.cs`:

```csharp
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
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*XrplBase58Tests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add XrplVanity.sln src/XrplVanity.Core tests/XrplVanity.Tests
git commit -m "feat: XRPL base58 codec and project scaffolding"
```

---

### Task 2: Address derivation and seed codec (known vector)

**Files:**
- Create: `src/XrplVanity.Core/Seed.cs`, `src/XrplVanity.Core/IAddressDeriver.cs`, `src/XrplVanity.Core/BouncyCastleDeriver.cs`
- Modify: `src/XrplVanity.Core/XrplVanity.Core.csproj` (add BouncyCastle.Cryptography)
- Test: `tests/XrplVanity.Tests/AddressDeriverTests.cs`

**Interfaces:**
- Consumes: `XrplBase58.Encode`, `XrplBase58.Decode`.
- Produces:
  - `XrplVanity.Core.Seed.Encode(ReadOnlySpan<byte> entropy16) -> string` (returns the `sEd...` seed).
  - `XrplVanity.Core.Seed.DecodeEntropy(string seed) -> byte[]` (returns the 16 entropy bytes).
  - `interface XrplVanity.Core.IAddressDeriver { int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest); }` (writes the classic address chars, returns length; `dest` >= 40).
  - `sealed class XrplVanity.Core.BouncyCastleDeriver : IAddressDeriver` (parameterless ctor; one instance per thread — not thread-safe).

- [ ] **Step 1: Add the crypto package**

```bash
dotnet add src/XrplVanity.Core/XrplVanity.Core.csproj package BouncyCastle.Cryptography --version 2.7.0
```

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.Tests/AddressDeriverTests.cs`:

```csharp
using System;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class AddressDeriverTests
{
    [Fact]
    public void Seed_Roundtrips_Through_Entropy()
    {
        const string seed = "sEdTuRgwYiEcubPJfRJUArVyn7AXkVj";
        byte[] entropy = Seed.DecodeEntropy(seed);

        Assert.Equal(16, entropy.Length);
        Assert.Equal(seed, Seed.Encode(entropy));
    }

    [Fact]
    public void Derives_Known_Address_From_Known_Seed()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        IAddressDeriver deriver = new BouncyCastleDeriver();

        Span<char> buffer = stackalloc char[48];
        int len = deriver.DeriveAddress(entropy, buffer);
        string address = new string(buffer[..len]);

        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", address);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*AddressDeriverTests"`
Expected: build error / FAIL — `Seed`, `IAddressDeriver`, `BouncyCastleDeriver` do not exist.

- [ ] **Step 4: Write minimal implementation**

`src/XrplVanity.Core/Seed.cs`:

```csharp
using System;
using System.Security.Cryptography;

namespace XrplVanity.Core;

/// <summary>Encodes/decodes the XRPL Ed25519 family seed (sEd...).</summary>
public static class Seed
{
    // Ed25519 seed version prefix: 0x01 0xE1 0x4B.
    private static readonly byte[] Prefix = { 0x01, 0xE1, 0x4B };

    public static string Encode(ReadOnlySpan<byte> entropy16)
    {
        Span<byte> payload = stackalloc byte[23];
        Prefix.CopyTo(payload);
        entropy16.CopyTo(payload[3..]);
        AppendChecksum(payload[..19], payload[19..]);

        Span<char> buffer = stackalloc char[64];
        int len = XrplBase58.Encode(payload, buffer);
        return new string(buffer[..len]);
    }

    public static byte[] DecodeEntropy(string seed)
    {
        byte[] raw = XrplBase58.Decode(seed);
        // raw = 3-byte prefix + 16 entropy + 4 checksum
        byte[] entropy = new byte[16];
        Array.Copy(raw, 3, entropy, 0, 16);
        return entropy;
    }

    internal static void AppendChecksum(ReadOnlySpan<byte> body, Span<byte> checksum4)
    {
        Span<byte> h1 = stackalloc byte[32];
        Span<byte> h2 = stackalloc byte[32];
        SHA256.HashData(body, h1);
        SHA256.HashData(h1, h2);
        h2[..4].CopyTo(checksum4);
    }
}
```

`src/XrplVanity.Core/IAddressDeriver.cs`:

```csharp
using System;

namespace XrplVanity.Core;

/// <summary>Derives a classic XRPL address from 16 bytes of entropy.</summary>
public interface IAddressDeriver
{
    /// <summary>Writes the address into <paramref name="dest"/> and returns its length.</summary>
    int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest);
}
```

`src/XrplVanity.Core/BouncyCastleDeriver.cs`:

```csharp
using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;

namespace XrplVanity.Core;

/// <summary>
/// Managed Ed25519 address derivation. Holds a reusable RIPEMD-160 instance, so
/// each worker thread must use its own <see cref="BouncyCastleDeriver"/>.
/// </summary>
public sealed class BouncyCastleDeriver : IAddressDeriver
{
    private readonly RipeMD160Digest _ripemd = new();

    public int DeriveAddress(ReadOnlySpan<byte> entropy16, Span<char> dest)
    {
        Span<byte> sha512 = stackalloc byte[64];
        SHA512.HashData(entropy16, sha512);

        Span<byte> pub33 = stackalloc byte[33];
        pub33[0] = 0xED;
        Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(sha512[..32], pub33[1..]);

        Span<byte> sha256 = stackalloc byte[32];
        SHA256.HashData(pub33, sha256);

        Span<byte> payload = stackalloc byte[25];
        payload[0] = 0x00; // AccountID version
        _ripemd.BlockUpdate(sha256);
        _ripemd.DoFinal(payload[1..]);

        Seed.AppendChecksum(payload[..21], payload[21..]);

        return XrplBase58.Encode(payload, dest);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*AddressDeriverTests"`
Expected: PASS (2 tests). The known-vector test is the primary correctness anchor.

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.Core tests/XrplVanity.Tests
git commit -m "feat: Ed25519 address derivation and seed codec, pinned to known vector"
```

---

### Task 3: Cross-check against the Xrpl.Keypairs SDK

**Files:**
- Modify: `tests/XrplVanity.Tests/XrplVanity.Tests.csproj` (add Xrpl.Keypairs)
- Test: `tests/XrplVanity.Tests/CrossCheckTests.cs`

**Interfaces:**
- Consumes: `Seed.Encode`, `BouncyCastleDeriver.DeriveAddress`, and the SDK's `Xrpl.Keypairs.XrplKeypairs`.
- Produces: nothing (test-only).

Note: `Xrpl.Keypairs` pulls `Portable.BouncyCastle` 1.9.0, whose assembly is `BouncyCastle.Crypto.dll` — a different name from Core's `BouncyCastle.Cryptography.dll`, so both load side by side with no alias.

- [ ] **Step 1: Add the SDK package**

```bash
dotnet add tests/XrplVanity.Tests/XrplVanity.Tests.csproj package Xrpl.Keypairs --version 10.9.0
```

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.Tests/CrossCheckTests.cs`:

```csharp
using System;
using System.Security.Cryptography;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CrossCheckTests
{
    [Fact]
    public void Derived_Address_Matches_Sdk_For_Random_Seeds()
    {
        IAddressDeriver deriver = new BouncyCastleDeriver();

        for (int i = 0; i < 500; i++)
        {
            byte[] entropy = RandomNumberGenerator.GetBytes(16);

            Span<char> buffer = stackalloc char[48];
            int len = deriver.DeriveAddress(entropy, buffer);
            string ours = new string(buffer[..len]);

            string sdkSeed = Xrpl.Keypairs.XrplKeypairs.GenerateSeed(entropy, "ed25519");
            Xrpl.Keypairs.IXrplKeyPair kp = Xrpl.Keypairs.XrplKeypairs.DeriveKeypair(sdkSeed);
            string sdk = Xrpl.Keypairs.XrplKeypairs.DeriveAddress(kp.Id());

            Assert.Equal(sdk, ours);
            Assert.Equal(sdkSeed, Seed.Encode(entropy));
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails or errors first**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CrossCheckTests"`
Expected before the package is restored: build error. After Step 1, the test should compile; if any assembly-load error for `BouncyCastle.Crypto` appears, confirm `Xrpl.Keypairs` restored its `Portable.BouncyCastle` 1.9.0 dependency (it is transitive and should restore automatically).

- [ ] **Step 4: No implementation needed**

This task validates existing code. If the assertion fails, the derivation in Task 2 is wrong — fix `BouncyCastleDeriver`, not the test.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CrossCheckTests"`
Expected: PASS (1 test, 500 iterations).

- [ ] **Step 6: Commit**

```bash
git add tests/XrplVanity.Tests
git commit -m "test: cross-check derivation against Xrpl.Keypairs over random seeds"
```

---

### Task 4: Pattern normalization into the base58 alphabet

**Files:**
- Create: `src/XrplVanity.Core/PatternNormalizer.cs`
- Test: `tests/XrplVanity.Tests/PatternNormalizerTests.cs`

**Interfaces:**
- Consumes: `XrplBase58.Alphabet`.
- Produces:
  - `readonly record struct XrplVanity.Core.NormalizedPattern(string Value, bool Reachable, string Original)`.
  - `XrplVanity.Core.PatternNormalizer.Normalize(string pattern) -> NormalizedPattern`. Applies `l->L`, `0->o`, `O->o`, `I->i`; if any remaining character is outside `XrplBase58.Alphabet`, `Reachable` is `false` and `Value` holds the normalized text so far.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/PatternNormalizerTests.cs`:

```csharp
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class PatternNormalizerTests
{
    [Fact]
    public void Maps_Non_Alphabet_Lookalikes()
    {
        NormalizedPattern p = PatternNormalizer.Normalize("hello");
        Assert.True(p.Reachable);
        Assert.Equal("heLLo", p.Value); // 'l' -> 'L'
    }

    [Fact]
    public void Maps_Zero_And_Capital_O_And_Capital_I()
    {
        Assert.Equal("ooi", PatternNormalizer.Normalize("0Oi").Value);
        Assert.Equal("i", PatternNormalizer.Normalize("I").Value);
    }

    [Fact]
    public void Flags_Unreachable_Characters()
    {
        NormalizedPattern p = PatternNormalizer.Normalize("bit@coin");
        Assert.False(p.Reachable); // '@' cannot occur in an address
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternNormalizerTests"`
Expected: build error / FAIL — `PatternNormalizer` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/XrplVanity.Core/PatternNormalizer.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternNormalizerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core tests/XrplVanity.Tests
git commit -m "feat: normalize vanity patterns into the base58 alphabet"
```

---

### Task 5: Pattern matcher

**Files:**
- Create: `src/XrplVanity.Core/MatchPosition.cs`, `src/XrplVanity.Core/PatternSet.cs`
- Test: `tests/XrplVanity.Tests/PatternSetTests.cs`

**Interfaces:**
- Consumes: `NormalizedPattern`, `PatternNormalizer.Normalize`.
- Produces:
  - `enum XrplVanity.Core.MatchPosition { Starts, Ends, Contains }`.
  - `sealed class XrplVanity.Core.PatternSet` with:
    - `PatternSet.Builder` fluent builder: `Add(MatchPosition position, string pattern, bool ignoreCase) -> Builder`, `Build() -> PatternSet`.
    - `bool TryMatch(ReadOnlySpan<char> address, out MatchPosition position, out string pattern)` — matches against the address **excluding the leading `r`**.
    - `IReadOnlyList<NormalizedPattern> Unreachable { get; }` — patterns that were skipped.

Match semantics: the address is matched from index 1 (after `r`). `Starts` compares the slice beginning at index 1; `Ends` compares the trailing slice; `Contains` searches anywhere from index 1. Case-insensitivity is realized by expanding each pattern into upper-case and title-case variants at build time (hot loop stays an ordinal lookup).

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/PatternSetTests.cs`:

```csharp
using System;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class PatternSetTests
{
    [Fact]
    public void Matches_Prefix_After_Leading_R_Case_Insensitive()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: true)
            .Build();

        bool matched = set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK",
            out MatchPosition pos, out string pattern);

        Assert.True(matched);
        Assert.Equal(MatchPosition.Starts, pos);
        Assert.Equal("Disk", pattern);
    }

    [Fact]
    public void Prefix_Does_Not_Match_When_Case_Sensitive()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: false)
            .Build();

        Assert.False(set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void Matches_Suffix_And_Contains()
    {
        PatternSet ends = new PatternSet.Builder()
            .Add(MatchPosition.Ends, "HGK", ignoreCase: false)
            .Build();
        Assert.True(ends.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));

        PatternSet contains = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "qgxo", ignoreCase: true)
            .Build();
        Assert.True(contains.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void Collects_Unreachable_Patterns()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "bit@coin", ignoreCase: true)
            .Build();

        Assert.Single(set.Unreachable);
        Assert.False(set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternSetTests"`
Expected: build error / FAIL — `MatchPosition`, `PatternSet` do not exist.

- [ ] **Step 3: Write minimal implementation**

`src/XrplVanity.Core/MatchPosition.cs`:

```csharp
namespace XrplVanity.Core;

public enum MatchPosition
{
    Starts,
    Ends,
    Contains,
}
```

`src/XrplVanity.Core/PatternSet.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace XrplVanity.Core;

/// <summary>Matches an address (excluding the leading 'r') against bucketed patterns.</summary>
public sealed class PatternSet
{
    private readonly Bucket[] _starts;
    private readonly Bucket[] _ends;
    private readonly List<string> _contains;
    private readonly List<NormalizedPattern> _unreachable;

    private PatternSet(Bucket[] starts, Bucket[] ends, List<string> contains,
        List<NormalizedPattern> unreachable)
    {
        _starts = starts;
        _ends = ends;
        _contains = contains;
        _unreachable = unreachable;
    }

    public IReadOnlyList<NormalizedPattern> Unreachable => _unreachable;

    public bool TryMatch(ReadOnlySpan<char> address, out MatchPosition position, out string pattern)
    {
        ReadOnlySpan<char> body = address[1..]; // drop leading 'r'

        if (TryMatchAffix(_starts, body, fromStart: true, out pattern))
        {
            position = MatchPosition.Starts;
            return true;
        }

        if (TryMatchAffix(_ends, body, fromStart: false, out pattern))
        {
            position = MatchPosition.Ends;
            return true;
        }

        foreach (string needle in _contains)
        {
            if (body.IndexOf(needle) >= 0)
            {
                position = MatchPosition.Contains;
                pattern = needle;
                return true;
            }
        }

        position = default;
        pattern = string.Empty;
        return false;
    }

    private static bool TryMatchAffix(Bucket[] buckets, ReadOnlySpan<char> body, bool fromStart,
        out string pattern)
    {
        foreach (Bucket bucket in buckets)
        {
            if (bucket.Length > body.Length)
            {
                continue;
            }

            ReadOnlySpan<char> slice = fromStart
                ? body[..bucket.Length]
                : body[^bucket.Length..];

            if (bucket.Lookup.Contains(slice))
            {
                pattern = new string(slice);
                return true;
            }
        }

        pattern = string.Empty;
        return false;
    }

    private readonly struct Bucket
    {
        public Bucket(int length, HashSet<string> values)
        {
            Length = length;
            Values = values;
            Lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public int Length { get; }
        public HashSet<string> Values { get; }
        public HashSet<string>.AlternateLookup<ReadOnlySpan<char>> Lookup { get; }
    }

    public sealed class Builder
    {
        private readonly Dictionary<int, HashSet<string>> _starts = new();
        private readonly Dictionary<int, HashSet<string>> _ends = new();
        private readonly List<string> _contains = new();
        private readonly List<NormalizedPattern> _unreachable = new();

        public Builder Add(MatchPosition position, string pattern, bool ignoreCase)
        {
            NormalizedPattern normalized = PatternNormalizer.Normalize(pattern);
            if (!normalized.Reachable)
            {
                _unreachable.Add(normalized);
                return this;
            }

            foreach (string variant in Variants(normalized.Value, ignoreCase))
            {
                switch (position)
                {
                    case MatchPosition.Starts:
                        AddToBuckets(_starts, variant);
                        break;
                    case MatchPosition.Ends:
                        AddToBuckets(_ends, variant);
                        break;
                    case MatchPosition.Contains:
                        _contains.Add(variant);
                        break;
                }
            }

            return this;
        }

        public PatternSet Build()
        {
            return new PatternSet(ToBuckets(_starts), ToBuckets(_ends), _contains, _unreachable);
        }

        private static IEnumerable<string> Variants(string value, bool ignoreCase)
        {
            if (!ignoreCase)
            {
                yield return value;
                yield break;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            string upper = value.ToUpperInvariantSafe();
            string title = value.Length == 0
                ? value
                : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

            foreach (string v in new[] { value, upper, title })
            {
                // patterns are already normalized; re-normalize the case variant so
                // characters like 'L'/'l' stay inside the alphabet
                NormalizedPattern n = PatternNormalizer.Normalize(v);
                if (n.Reachable && seen.Add(n.Value))
                {
                    yield return n.Value;
                }
            }
        }

        private static void AddToBuckets(Dictionary<int, HashSet<string>> buckets, string value)
        {
            if (!buckets.TryGetValue(value.Length, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                buckets[value.Length] = set;
            }

            set.Add(value);
        }

        private static Bucket[] ToBuckets(Dictionary<int, HashSet<string>> buckets)
        {
            List<Bucket> list = new(buckets.Count);
            foreach (KeyValuePair<int, HashSet<string>> kv in buckets)
            {
                list.Add(new Bucket(kv.Key, kv.Value));
            }

            return list.ToArray();
        }
    }
}

internal static class StringCaseExtensions
{
    public static string ToUpperInvariantSafe(this string value) =>
        value.ToUpperInvariant();
}
```

Note: the `ToUpperInvariantSafe` extension exists only to keep the `Variants` code readable; if you prefer, inline `value.ToUpperInvariant()` and delete the extension.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternSetTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core tests/XrplVanity.Tests
git commit -m "feat: bucketed prefix/suffix/contains matcher with case expansion"
```

---

### Task 6: Candidate evaluator

**Files:**
- Create: `src/XrplVanity.Core/Match.cs`, `src/XrplVanity.Core/CandidateEvaluator.cs`
- Test: `tests/XrplVanity.Tests/CandidateEvaluatorTests.cs`

**Interfaces:**
- Consumes: `IAddressDeriver`, `PatternSet.TryMatch`, `Seed.Encode`.
- Produces:
  - `readonly record struct XrplVanity.Core.Match(string Address, string Seed, string Pattern, MatchPosition Position)`.
  - `sealed class XrplVanity.Core.CandidateEvaluator` — ctor `(IAddressDeriver deriver, PatternSet patterns)`; `bool TryEvaluate(ReadOnlySpan<byte> entropy16, out Match match)`. One instance per thread (owns the deriver).

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/CandidateEvaluatorTests.cs`:

```csharp
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CandidateEvaluatorTests
{
    [Fact]
    public void Reports_Match_With_Seed_For_Known_Vector()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        PatternSet patterns = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: true)
            .Build();
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);

        bool matched = evaluator.TryEvaluate(entropy, out Match match);

        Assert.True(matched);
        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", match.Address);
        Assert.Equal("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj", match.Seed);
        Assert.Equal(MatchPosition.Starts, match.Position);
    }

    [Fact]
    public void Reports_No_Match_When_Patterns_Absent()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        PatternSet patterns = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "zzzz", ignoreCase: true)
            .Build();
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);

        Assert.False(evaluator.TryEvaluate(entropy, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CandidateEvaluatorTests"`
Expected: build error / FAIL — `Match`, `CandidateEvaluator` do not exist.

- [ ] **Step 3: Write minimal implementation**

`src/XrplVanity.Core/Match.cs`:

```csharp
namespace XrplVanity.Core;

public readonly record struct Match(string Address, string Seed, string Pattern, MatchPosition Position);
```

`src/XrplVanity.Core/CandidateEvaluator.cs`:

```csharp
using System;

namespace XrplVanity.Core;

/// <summary>
/// Evaluates one entropy value: derives its address and reports a <see cref="Match"/>
/// if any pattern hits. Not thread-safe (owns a deriver); use one per worker thread.
/// </summary>
public sealed class CandidateEvaluator
{
    private readonly IAddressDeriver _deriver;
    private readonly PatternSet _patterns;

    public CandidateEvaluator(IAddressDeriver deriver, PatternSet patterns)
    {
        _deriver = deriver;
        _patterns = patterns;
    }

    public bool TryEvaluate(ReadOnlySpan<byte> entropy16, out Match match)
    {
        Span<char> buffer = stackalloc char[48];
        int len = _deriver.DeriveAddress(entropy16, buffer);
        ReadOnlySpan<char> address = buffer[..len];

        if (_patterns.TryMatch(address, out MatchPosition position, out string pattern))
        {
            match = new Match(new string(address), Seed.Encode(entropy16), pattern, position);
            return true;
        }

        match = default;
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CandidateEvaluatorTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core tests/XrplVanity.Tests
git commit -m "feat: single-candidate evaluator (the GPU-shared semantics unit)"
```

---

### Task 7: CLI option parsing

**Files:**
- Create: `src/XrplVanity.Cli/XrplVanity.Cli.csproj`, `src/XrplVanity.Cli/CliOptions.cs`
- Modify: `XrplVanity.sln` (add the CLI project); `tests/XrplVanity.Tests/XrplVanity.Tests.csproj` (reference the CLI project)
- Test: `tests/XrplVanity.Tests/CliOptionsTests.cs`

**Interfaces:**
- Consumes: `MatchPosition`.
- Produces:
  - `sealed class XrplVanity.Cli.CliOptions` with properties: `IReadOnlyList<(MatchPosition Position, string Pattern)> Patterns`, `bool CaseSensitive`, `int Threads`, `string? OutFile`, `int? Count`, `string? ListFile`, `int ListMinLength`.
  - `static CliOptions CliOptions.Parse(string[] args) -> CliOptions`.

- [ ] **Step 1: Create the CLI project and wire references**

```bash
cd xrpl-vanity
dotnet new console -n XrplVanity.Cli -o src/XrplVanity.Cli -f net10.0
rm -f src/XrplVanity.Cli/Program.cs
dotnet sln add src/XrplVanity.Cli/XrplVanity.Cli.csproj
dotnet add src/XrplVanity.Cli/XrplVanity.Cli.csproj reference src/XrplVanity.Core/XrplVanity.Core.csproj
dotnet add tests/XrplVanity.Tests/XrplVanity.Tests.csproj reference src/XrplVanity.Cli/XrplVanity.Cli.csproj
```

Set `XrplVanity.Cli.csproj` `PropertyGroup`: `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, and keep `<OutputType>Exe</OutputType>`.

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.Tests/CliOptionsTests.cs`:

```csharp
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Positional_Words_Default_To_Starts()
    {
        CliOptions options = CliOptions.Parse(new[] { "ripple", "hodl" });

        Assert.Equal(2, options.Patterns.Count);
        Assert.All(options.Patterns, p => Assert.Equal(MatchPosition.Starts, p.Position));
        Assert.False(options.CaseSensitive);
    }

    [Fact]
    public void Parses_Position_Flags_And_Options()
    {
        CliOptions options = CliOptions.Parse(new[]
        {
            "--ends", "xyz", "--contains", "dao", "--case-sensitive", "--threads", "8", "--count", "3",
        });

        Assert.Contains(options.Patterns, p => p.Position == MatchPosition.Ends && p.Pattern == "xyz");
        Assert.Contains(options.Patterns, p => p.Position == MatchPosition.Contains && p.Pattern == "dao");
        Assert.True(options.CaseSensitive);
        Assert.Equal(8, options.Threads);
        Assert.Equal(3, options.Count);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliOptionsTests"`
Expected: build error / FAIL — `CliOptions` does not exist.

- [ ] **Step 4: Write minimal implementation**

`src/XrplVanity.Cli/CliOptions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XrplVanity.Core;

namespace XrplVanity.Cli;

public sealed class CliOptions
{
    public required IReadOnlyList<(MatchPosition Position, string Pattern)> Patterns { get; init; }
    public bool CaseSensitive { get; init; }
    public int Threads { get; init; } = Environment.ProcessorCount;
    public string? OutFile { get; init; }
    public int? Count { get; init; }
    public string? ListFile { get; init; }
    public int ListMinLength { get; init; } = 4;

    public static CliOptions Parse(string[] args)
    {
        List<(MatchPosition, string)> patterns = new();
        bool caseSensitive = false;
        int threads = Environment.ProcessorCount;
        string? outFile = null;
        int? count = null;
        string? listFile = null;
        int listMinLength = 4;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--starts":
                    patterns.Add((MatchPosition.Starts, Next(args, ref i, arg)));
                    break;
                case "--ends":
                    patterns.Add((MatchPosition.Ends, Next(args, ref i, arg)));
                    break;
                case "--contains":
                    patterns.Add((MatchPosition.Contains, Next(args, ref i, arg)));
                    break;
                case "--case-sensitive":
                    caseSensitive = true;
                    break;
                case "--threads":
                    threads = int.Parse(Next(args, ref i, arg));
                    break;
                case "--out":
                    outFile = Next(args, ref i, arg);
                    break;
                case "--count":
                    count = int.Parse(Next(args, ref i, arg));
                    break;
                case "--list":
                    listFile = Next(args, ref i, arg);
                    // optional numeric min length may follow
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int min))
                    {
                        listMinLength = min;
                        i++;
                    }
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }
                    patterns.Add((MatchPosition.Starts, arg));
                    break;
            }
        }

        return new CliOptions
        {
            Patterns = patterns,
            CaseSensitive = caseSensitive,
            Threads = threads,
            OutFile = outFile,
            Count = count,
            ListFile = listFile,
            ListMinLength = listMinLength,
        };
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[++i];
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliOptionsTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.Cli tests/XrplVanity.Tests XrplVanity.sln
git commit -m "feat: CLI option parsing"
```

---

### Task 8: CLI runtime (threads, output) and README

**Files:**
- Create: `src/XrplVanity.Cli/Program.cs`, `README.md`, `LICENSE`
- Test: `tests/XrplVanity.Tests/CliRuntimeTests.cs`

**Interfaces:**
- Consumes: `CliOptions`, `PatternSet.Builder`, `CandidateEvaluator`, `BouncyCastleDeriver`, `Match`.
- Produces:
  - `static class XrplVanity.Cli.SearchRunner` with `PatternSet BuildPatternSet(CliOptions options)` and `Match? SearchDeterministic(CliOptions options, IEnumerable<byte[]> entropySource)` — a headless search used by the test and by `Program`.
  - `Program.Main` wires CSPRNG entropy + threads onto `SearchRunner`.

The runtime is split so the searchable core (`SearchRunner`) is testable without spawning threads or reading `/dev/random`.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/CliRuntimeTests.cs`:

```csharp
using System.Collections.Generic;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliRuntimeTests
{
    [Fact]
    public void SearchDeterministic_Finds_Match_In_A_Fixed_Entropy_Stream()
    {
        CliOptions options = CliOptions.Parse(new[] { "disk" });
        byte[] winning = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");
        List<byte[]> stream = new()
        {
            new byte[16], // no match
            winning,      // matches "disk"
        };

        Match? found = SearchRunner.SearchDeterministic(options, stream);

        Assert.NotNull(found);
        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", found!.Value.Address);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliRuntimeTests"`
Expected: build error / FAIL — `SearchRunner` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/XrplVanity.Cli/Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;

namespace XrplVanity.Cli;

public static class SearchRunner
{
    public static PatternSet BuildPatternSet(CliOptions options)
    {
        PatternSet.Builder builder = new();
        foreach ((MatchPosition position, string pattern) in options.Patterns)
        {
            builder.Add(position, pattern, ignoreCase: !options.CaseSensitive);
        }

        if (options.ListFile is not null)
        {
            foreach (string line in File.ReadLines(options.ListFile))
            {
                string word = line.Trim();
                if (word.Length >= options.ListMinLength)
                {
                    builder.Add(MatchPosition.Starts, word, ignoreCase: !options.CaseSensitive);
                }
            }
        }

        return builder.Build();
    }

    public static Match? SearchDeterministic(CliOptions options, IEnumerable<byte[]> entropySource)
    {
        PatternSet patterns = BuildPatternSet(options);
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
        foreach (byte[] entropy in entropySource)
        {
            if (evaluator.TryEvaluate(entropy, out Match match))
            {
                return match;
            }
        }

        return null;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            Console.Error.WriteLine(
                $"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
        }

        Console.Error.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        long found = 0;
        long total = 0;
        object gate = new();
        using CancellationTokenSource cts = new();
        TextWriter? outFile = options.OutFile is null ? null : new StreamWriter(options.OutFile, append: true);

        void Worker()
        {
            CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
            byte[] pool = new byte[16 * 512];
            int poolPos = pool.Length;
            long local = 0;

            while (!cts.IsCancellationRequested)
            {
                if (poolPos >= pool.Length)
                {
                    RandomNumberGenerator.Fill(pool);
                    poolPos = 0;
                }

                ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, 16);
                poolPos += 16;
                local++;

                if (evaluator.TryEvaluate(entropy, out Match match))
                {
                    lock (gate)
                    {
                        Console.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
                        outFile?.WriteLine($"{match.Address}\t{match.Seed}");
                        outFile?.Flush();
                        long done = Interlocked.Increment(ref found);
                        if (options.Count is int limit && done >= limit)
                        {
                            cts.Cancel();
                        }
                    }
                }

                if ((local & 0xFFFF) == 0)
                {
                    Interlocked.Add(ref total, 0x10000);
                    local = 0;
                }
            }
        }

        Thread[] workers = new Thread[Math.Max(1, options.Threads)];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(Worker) { IsBackground = true };
            workers[t].Start();
        }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        outFile?.Dispose();
        return 0;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliRuntimeTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Run the whole suite**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: all tests PASS.

- [ ] **Step 6: Smoke-test the CLI end to end**

Run: `dotnet run --project src/XrplVanity.Cli -c Release -- --contains xrp --count 1`
Expected: prints the secret warning, then one `Address: rXXXX...\tSeed: sEd...` line containing `xrp`, then exits.

- [ ] **Step 7: Write README and LICENSE**

Create `README.md` (English) covering: what it is, that it is Ed25519-only, the three match modes with the example commands from the spec, the `--threads/--out/--count/--list` flags, the secret-handling warning, and a "planned: GPU backend" note. Add an Apache-2.0 `LICENSE` (the original tool this is inspired by is Apache-2.0; acknowledge `nhartner/xrp-vanity-address` in the README).

- [ ] **Step 8: Commit**

```bash
git add src/XrplVanity.Cli tests/XrplVanity.Tests README.md LICENSE
git commit -m "feat: multithreaded CLI runtime, secret warning, README"
```

---

## Notes for the executor

- Do not publish the repository, push, or open a pull request. Everything is local until the user explicitly says otherwise.
- If any cross-check assertion (Task 3) fails, the bug is in `BouncyCastleDeriver`, not in the SDK or the test.
- The one-off benchmark and GPU spike code stays in the scratchpad; it is not part of this repository.
