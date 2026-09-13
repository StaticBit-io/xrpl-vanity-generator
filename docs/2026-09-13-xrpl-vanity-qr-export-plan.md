# QR export to the StaticBit wallet — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A found wallet, or a selection of them, can be shown as a QR code the StaticBit mobile wallet imports by scanning, with App Store and Google Play links under it.

**Architecture:** The payload format is pure code with no UI and no new packages — `Base45` and `StaticBitBackup` go into `XrplVanity.Core`, tested in `XrplVanity.Tests`. The application layer adds one package (`QRCoder`, used only for its module matrix), a `QrCode` component that renders that matrix as inline SVG, and a `QrExportDialog`. `ResultsList` only raises a request; the pages own the dialog, because nothing inside `.content-scroll` can make the page around it inert.

**Tech Stack:** .NET 10, Blazor (MAUI BlazorWebView host), xUnit v3 with the built-in `Assert`, bUnit 2.9.0, QRCoder (MIT). `DeflateStream`, `AesGcm` and `Rfc2898DeriveBytes` are in-box — the container needs no package at all.

**Spec:** `docs/2026-09-13-xrpl-vanity-qr-export-design.md`

## Global Constraints

- **Tests run with `dotnet run`, never `dotnet test`** — `dotnet test` does not work with xunit.v3 on net10. Whole suite: `dotnet run --project tests/XrplVanity.Tests -c Release`. One class: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*Base45Tests"`. The component suite is `tests/XrplVanity.App.Tests`.
- **The build must stay warning-free.** `dotnet build XrplVanity.sln -c Release` prints only `Сборка успешно завершена.`
- **No FluentAssertions.** xUnit's built-in `Assert` only.
- Explicit types instead of `var`. PascalCase members, `_camelCase` private fields. Comments in English, and only where the code cannot say it itself.
- **Container constants, verbatim:** magic `SBW1`; `version` = 1; `kdfId` = 0 without a password and 1 with one; PBKDF2-HMAC-SHA256, **600000** iterations, 32-byte key; salt 16 bytes; nonce 12 bytes; tag 16 bytes; associated data = the first **26** bytes; raw DEFLATE at `CompressionLevel.Optimal`; one QR holds **1234** container bytes.
- **Store URLs, verbatim:** `https://apps.apple.com/us/app/staticbit/id6745529426` and `https://play.google.com/store/apps/details?id=io.staticbit.wallet`.
- **Nothing is written to disk**, in any task. No "save as PNG", no share, no clipboard copy of the code.

---

### Task 1: Base45

**Files:**
- Create: `src/XrplVanity.Core/Base45.cs`
- Test: `tests/XrplVanity.Tests/Base45Tests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class Base45` with `public static string Encode(ReadOnlySpan<byte> data)`. Encode only — nothing in this feature decodes.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Base45Tests.cs`:

```csharp
using System.Text;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class Base45Tests
{
    [Theory]
    [InlineData("AB", "BB8")]
    [InlineData("Hello!!", "%69 VD92EX0")]
    [InlineData("base-45", "UJCLQE7W581")]
    [InlineData("ietf!", "QED8WEX0")]
    public void Rfc9285_Vectors_Encode_As_Published(string text, string expected)
    {
        Assert.Equal(expected, Base45.Encode(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void An_Odd_Trailing_Byte_Becomes_Two_Characters()
    {
        // A pair becomes three characters, a lone byte two: 3 bytes therefore give 5.
        Assert.Equal(5, Base45.Encode(new byte[] { 0x53, 0x42, 0x57 }).Length);
    }

    [Fact]
    public void Empty_Input_Encodes_As_An_Empty_String()
    {
        Assert.Equal(string.Empty, Base45.Encode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void The_Magic_Bytes_Encode_As_The_Prefix_The_Wallet_Looks_For()
    {
        Assert.Equal("TNA11B", Base45.Encode("SBW1"u8));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*Base45Tests"`
Expected: a build failure, `error CS0103: the name "Base45" does not exist`.

- [ ] **Step 3: Write the implementation**

`src/XrplVanity.Core/Base45.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*Base45Tests"`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core/Base45.cs tests/XrplVanity.Tests/Base45Tests.cs
git commit -m "feat(core): Base45 encoding, the alphabet a backup QR is written in"
```

---

### Task 2: The StaticBit backup container

**Files:**
- Create: `src/XrplVanity.Core/StaticBitBackup.cs`
- Test: `tests/XrplVanity.Tests/StaticBitBackupTests.cs`

**Interfaces:**
- Consumes: `Base45.Encode` from Task 1.
- Produces:
  - `public readonly record struct BackupWallet(string Seed, string Address, string? Description)`
  - `public static class StaticBitBackup` with
    `public const int MaxQrContainerBytes = 1234;`
    `public static byte[] Pack(IReadOnlyList<BackupWallet> wallets, string? password)`
    `public static string ToQrText(IReadOnlyList<BackupWallet> wallets, string? password)`
    `internal static string BuildJson(IReadOnlyList<BackupWallet> wallets)`
  - `InternalsVisibleTo` for `BuildJson`: `src/XrplVanity.Core/XrplVanity.Core.csproj` must gain `<ItemGroup><InternalsVisibleTo Include="XrplVanity.Tests" /></ItemGroup>` if it has none.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/StaticBitBackupTests.cs`:

```csharp
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class StaticBitBackupTests
{
    private static readonly BackupWallet One =
        new("sEdTM1uX8pu2do5XvTnutH6HsouMaM2", "rLczgQHxPhWtjkaQqn3Q6UM8AbRbbRvs5K", "static..bit");

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using MemoryStream input = new(compressed.ToArray());
        using DeflateStream deflate = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void The_Json_Is_Exactly_What_The_Wallet_Requires()
    {
        Assert.Equal(
            "{\"wallets\":[{\"keyValue\":\"sEdTM1uX8pu2do5XvTnutH6HsouMaM2\"," +
            "\"classicAddress\":\"rLczgQHxPhWtjkaQqn3Q6UM8AbRbbRvs5K\"," +
            "\"keyType\":1,\"algorithm\":\"ed25519\",\"description\":\"static..bit\"}]}",
            StaticBitBackup.BuildJson(new[] { One }));
    }

    [Fact]
    public void A_Wallet_Without_A_Description_Leaves_The_Field_Out_Rather_Than_Writing_Null()
    {
        string json = StaticBitBackup.BuildJson(new[] { One with { Description = null } });
        Assert.DoesNotContain("description", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Without_A_Password_The_Header_Is_Six_Bytes_And_The_Payload_Follows_It()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, password: null);

        Assert.Equal("SBW1"u8.ToArray(), container[..4]);
        Assert.Equal(1, container[4]);
        Assert.Equal(0, container[5]);
        Assert.Equal(StaticBitBackup.BuildJson(new[] { One }), Encoding.UTF8.GetString(Inflate(container.AsSpan(6))));
    }

    [Fact]
    public void With_A_Password_The_Header_Names_The_Kdf_And_Its_Iterations()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        Assert.Equal("SBW1"u8.ToArray(), container[..4]);
        Assert.Equal(1, container[4]);
        Assert.Equal(1, container[5]);
        Assert.Equal(600_000u, BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(6, 4)));
        // 26 header + 12 nonce + 16 tag around a ciphertext the same length as the plaintext.
        Assert.True(container.Length > 26 + 12 + 16);
    }

    [Fact]
    public void A_Password_Container_Decrypts_With_The_Header_As_Associated_Data()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        byte[] header = container[..26];
        byte[] salt = container[10..26];
        byte[] nonce = container[26..38];
        byte[] tag = container[^16..];
        byte[] ciphertext = container[38..^16];

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("correct horse"), salt, 600_000, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm aes = new(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, header);

        Assert.Equal(StaticBitBackup.BuildJson(new[] { One }), Encoding.UTF8.GetString(Inflate(plaintext)));
    }

    [Fact]
    public void A_Wrong_Password_Cannot_Open_The_Container()
    {
        byte[] container = StaticBitBackup.Pack(new[] { One }, "correct horse");

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("wrong horse"), container[10..26], 600_000, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[container.Length - 26 - 12 - 16];
        using AesGcm aes = new(key, 16);

        Assert.Throws<AuthenticationTagMismatchException>(
            () => aes.Decrypt(container[26..38], container[38..^16], container[^16..], plaintext, container[..26]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("correct horse")]
    public void Every_Container_Starts_With_The_Prefix_The_Wallets_Scanner_Looks_For(string? password)
    {
        Assert.StartsWith("TNA11B", StaticBitBackup.ToQrText(new[] { One }, password), StringComparison.Ordinal);
    }

    [Fact]
    public void An_Empty_Set_Is_Refused_Rather_Than_Packed()
    {
        Assert.Throws<ArgumentException>(() => StaticBitBackup.Pack(Array.Empty<BackupWallet>(), null));
    }

    [Theory]
    [InlineData(null, 22)]
    [InlineData("correct horse", 21)]
    public void The_Measured_Capacity_Of_One_Code_Holds(string? password, int wallets)
    {
        // The design's table. If DEFLATE or the container ever moves, this test is what moves with
        // it — and the number in docs/2026-09-13-xrpl-vanity-qr-export-design.md moves too.
        Assert.True(StaticBitBackup.Pack(Many(wallets), password).Length <= StaticBitBackup.MaxQrContainerBytes);
        Assert.True(StaticBitBackup.Pack(Many(wallets + 1), password).Length > StaticBitBackup.MaxQrContainerBytes);
    }

    private static BackupWallet[] Many(int count)
    {
        BackupWallet[] wallets = new BackupWallet[count];
        for (int i = 0; i < count; i++)
        {
            // Distinct keys: identical entries would compress far better than real results do.
            wallets[i] = new BackupWallet(
                "sEd" + Base58Ish(26, i),
                "r" + Base58Ish(33, i + 1000),
                "static..bit");
        }

        return wallets;
    }

    private static string Base58Ish(int length, int seed)
    {
        const string Alphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";
        Random random = new(seed);
        char[] chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = Alphabet[random.Next(Alphabet.Length)];
        return new string(chars);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*StaticBitBackupTests"`
Expected: a build failure, `error CS0103: the name "StaticBitBackup" does not exist`.

- [ ] **Step 3: Write the implementation**

`src/XrplVanity.Core/StaticBitBackup.cs`:

```csharp
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XrplVanity.Core;

/// <summary>One found wallet, in the shape the StaticBit backup payload wants it.</summary>
public readonly record struct BackupWallet(string Seed, string Address, string? Description);

/// <summary>
/// Builds the backup container the StaticBit wallet's scanner restores from. The format belongs to
/// that application, not to this one: every constant here is copied from it, and the tests pin each
/// one, because nothing warns us when it changes. See
/// docs/2026-09-13-xrpl-vanity-qr-export-design.md.
/// </summary>
public static class StaticBitBackup
{
    /// <summary>What one QR code at correction level H holds: 1852 Base45 characters.</summary>
    public const int MaxQrContainerBytes = 1234;

    private const int Version = 1;
    private const int NoPasswordKdfId = 0;
    private const int Pbkdf2KdfId = 1;
    private const int Iterations = 600_000;
    private const int KeySize = 32;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>magic + version + kdfId — the whole header when there is no password.</summary>
    private const int HeaderPrefixLength = 6;

    /// <summary>prefix + iterations + salt — and the associated data AES-GCM authenticates.</summary>
    private const int PasswordHeaderLength = HeaderPrefixLength + 4 + SaltSize;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>The container as text, ready to draw. The container bytes do not outlive the call.</summary>
    public static string ToQrText(IReadOnlyList<BackupWallet> wallets, string? password)
    {
        byte[] container = Pack(wallets, password);
        try
        {
            return Base45.Encode(container);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(container);
        }
    }

    public static byte[] Pack(IReadOnlyList<BackupWallet> wallets, string? password)
    {
        ArgumentNullException.ThrowIfNull(wallets);
        if (wallets.Count == 0) throw new ArgumentException("A backup needs at least one wallet.", nameof(wallets));

        byte[] compressed = Deflate(Encoding.UTF8.GetBytes(BuildJson(wallets)));
        try
        {
            return password is null ? PackPlain(compressed) : PackEncrypted(compressed, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compressed);
        }
    }

    private static byte[] PackPlain(byte[] compressed)
    {
        byte[] container = new byte[HeaderPrefixLength + compressed.Length];
        WriteHeaderPrefix(container, NoPasswordKdfId);
        compressed.CopyTo(container, HeaderPrefixLength);
        return container;
    }

    private static byte[] PackEncrypted(byte[] compressed, string password)
    {
        byte[] container = new byte[PasswordHeaderLength + NonceSize + compressed.Length + TagSize];
        WriteHeaderPrefix(container, Pbkdf2KdfId);
        BinaryPrimitives.WriteUInt32BigEndian(container.AsSpan(HeaderPrefixLength, 4), Iterations);

        Span<byte> salt = container.AsSpan(HeaderPrefixLength + 4, SaltSize);
        RandomNumberGenerator.Fill(salt);
        Span<byte> nonce = container.AsSpan(PasswordHeaderLength, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Encrypt(
                nonce,
                compressed,
                container.AsSpan(PasswordHeaderLength + NonceSize, compressed.Length),
                container.AsSpan(container.Length - TagSize, TagSize),
                // The whole header, salt included: the wallet authenticates it and so must we.
                container.AsSpan(0, PasswordHeaderLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        return container;
    }

    private static void WriteHeaderPrefix(byte[] container, int kdfId)
    {
        "SBW1"u8.CopyTo(container);
        container[4] = Version;
        container[5] = (byte)kdfId;
    }

    /// <summary>Raw DEFLATE, RFC 1951 — no zlib wrapper, which is what the wallet inflates.</summary>
    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    internal static string BuildJson(IReadOnlyList<BackupWallet> wallets)
    {
        Entry[] entries = new Entry[wallets.Count];
        for (int i = 0; i < wallets.Count; i++)
        {
            BackupWallet wallet = wallets[i];
            entries[i] = new Entry(wallet.Seed, wallet.Address, wallet.Description);
        }

        return JsonSerializer.Serialize(new Payload(entries), SerializerOptions);
    }

    private sealed record Payload([property: JsonPropertyName("wallets")] IReadOnlyList<Entry> Wallets);

    /// <summary>
    /// keyType 1 is the wallet's AccountKeyType.Seed; 0 would mean a watch-only account and the
    /// wallet would refuse to derive from it. algorithm is a free string on that side, and this
    /// generator only ever produces Ed25519 family seeds.
    /// </summary>
    private sealed record Entry(
        [property: JsonPropertyName("keyValue")] string KeyValue,
        [property: JsonPropertyName("classicAddress")] string ClassicAddress,
        [property: JsonPropertyName("description")] string? Description)
    {
        [JsonPropertyName("keyType")] public int KeyType => 1;
        [JsonPropertyName("algorithm")] public string Algorithm => "ed25519";
    }
}
```

> Note on member order: `System.Text.Json` writes constructor parameters before other properties, so
> the serialized order is `keyValue, classicAddress, description, keyType, algorithm`. The test in
> Step 1 asserts `keyValue, classicAddress, keyType, algorithm, description`. **Field order does not
> matter to the importer** — it deserializes by name — so make the test match whatever the
> implementation actually emits rather than contorting the record. Run the test, read the actual
> string from the failure message, and correct the expected string once.

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*StaticBitBackupTests"`
Expected: `Failed: 0`. If `The_Measured_Capacity_Of_One_Code_Holds` fails, put the real numbers into the test **and** into the capacity table in the design document — the design says the test is what moves.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core/StaticBitBackup.cs src/XrplVanity.Core/XrplVanity.Core.csproj tests/XrplVanity.Tests/StaticBitBackupTests.cs
git commit -m "feat(core): the StaticBit backup container a wallet can restore from"
```

---

### Task 3: The QR code component

**Files:**
- Modify: `src/XrplVanity.App.Core/XrplVanity.App.Core.csproj` (add the `QRCoder` package)
- Create: `src/XrplVanity.App.Core/Components/QrCode.razor`, `QrCode.razor.cs`, `QrCode.razor.css`
- Test: `tests/XrplVanity.App.Tests/QrCodeTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: component `QrCode` with `[Parameter, EditorRequired] public string Text { get; set; }`. Renders one `<svg class="qr">` inside `<div class="qr-plate">`.

- [ ] **Step 1: Add the package and pin what it resolves to**

```bash
dotnet add src/XrplVanity.App.Core/XrplVanity.App.Core.csproj package QRCoder
```

Then open the `.csproj` and confirm the `PackageReference` carries an explicit `Version` (the repository pins every version explicitly and uses no central package management). Record the resolved version — it is needed again in Task 6.

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.App.Tests/QrCodeTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using Xunit;

namespace XrplVanity.App.Tests;

public class QrCodeTests : BunitContext
{
    [Fact]
    public void The_Code_Is_Inline_Svg_And_Never_An_Image()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));

        Assert.Single(cut.FindAll("svg.qr"));
        // An <img> is what makes "Save image as…" appear in WebView2; an inline SVG offers it nothing.
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void The_View_Box_Is_Square_And_Counts_Modules_Not_Pixels()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));

        string[] box = cut.Find("svg.qr").GetAttribute("viewBox")!.Split(' ');
        Assert.Equal("0", box[0]);
        Assert.Equal("0", box[1]);
        Assert.Equal(box[2], box[3]);
        Assert.True(int.Parse(box[2]) >= 21);   // version 1 is 21 modules across
    }

    [Fact]
    public void Longer_Text_Needs_A_Bigger_Symbol()
    {
        IRenderedComponent<QrCode> small = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));
        IRenderedComponent<QrCode> large = Render<QrCode>(p => p.Add(x => x.Text, new string('A', 600)));

        Assert.True(Modules(large) > Modules(small));

        static int Modules(IRenderedComponent<QrCode> cut) =>
            int.Parse(cut.Find("svg.qr").GetAttribute("viewBox")!.Split(' ')[2]);
    }

    [Fact]
    public void Changing_The_Text_Redraws_The_Code()
    {
        IRenderedComponent<QrCode> cut = Render<QrCode>(p => p.Add(x => x.Text, "TNA11B"));
        string before = cut.Find("svg.qr path").GetAttribute("d")!;

        cut.SetParametersAndRender(p => p.Add(x => x.Text, "TNA11BW50"));

        Assert.NotEqual(before, cut.Find("svg.qr path").GetAttribute("d"));
    }
}
```

- [ ] **Step 3: Run the test and watch it fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*QrCodeTests"`
Expected: a build failure, `error CS0246: the type or namespace name "QrCode" could not be found`.

- [ ] **Step 4: Write the implementation**

`src/XrplVanity.App.Core/Components/QrCode.razor`:

```razor
@* A light plate under the code: an inverted QR on this application's dark background is not
   reliably scannable, and the plate's padding is the quiet zone. *@
<div class="qr-plate">
  <svg class="qr" viewBox="0 0 @_modules @_modules" shape-rendering="crispEdges" role="img" aria-label="QR code">
    <path d="@_path" fill="#000000" />
  </svg>
</div>
```

`src/XrplVanity.App.Core/Components/QrCode.razor.cs`:

```csharp
using System.Collections;
using System.Text;
using Microsoft.AspNetCore.Components;
using QRCoder;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Draws a QR code as inline SVG. Not an &lt;img&gt; and not a PNG: an image in a WebView offers
/// "Save image as…" (and "Add to Photos" on the wallet's side), which would put a key on disk in
/// one click. One &lt;path&gt; of module-sized rectangles, so the whole symbol is a single node.
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
                if (row[x]) path.Append("M").Append(x).Append(' ').Append(y).Append("h1v1h-1z");
            }
        }

        return path.ToString();
    }
}
```

`src/XrplVanity.App.Core/Components/QrCode.razor.css`:

```css
/* The padding is the quiet zone, stated here rather than assumed of the encoder's matrix. */
.qr-plate { background: #FFFFFF; border-radius: var(--radius-chip); padding: 12px; width: max-content; margin: 0 auto; }
.qr { display: block; width: 260px; height: 260px; }
/* The code is not a picture to be lifted out of the window. */
.qr-plate { -webkit-user-select: none; user-select: none; }
```

- [ ] **Step 5: Run the test and watch it pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*QrCodeTests"`
Expected: `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.App.Core/XrplVanity.App.Core.csproj src/XrplVanity.App.Core/Components/QrCode.razor src/XrplVanity.App.Core/Components/QrCode.razor.cs src/XrplVanity.App.Core/Components/QrCode.razor.css tests/XrplVanity.App.Tests/QrCodeTests.cs
git commit -m "feat(app): a QR code component that draws itself as inline SVG"
```

---

### Task 4: The export dialog

**Files:**
- Create: `src/XrplVanity.App.Core/Components/QrExportDialog.razor`, `QrExportDialog.razor.cs`, `QrExportDialog.razor.css`
- Test: `tests/XrplVanity.App.Tests/QrExportDialogTests.cs`

**Interfaces:**
- Consumes: `StaticBitBackup`, `BackupWallet` (Task 2); component `QrCode` (Task 3); `TimeProvider` from DI, already registered by the host and faked with `FakeTimeProvider` in tests.
- Produces: component `QrExportDialog` with
  `[Parameter] public IReadOnlyList<SessionResult>? Rows { get; set; }` — non-null means visible
  `[Parameter] public EventCallback OnClose { get; set; }`

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.App.Tests/QrExportDialogTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class QrExportDialogTests : BunitContext
{
    private readonly FakeTimeProvider _time = new();

    private static SessionResult R(int i) => new(
        $"rLczgQHxPhWtjkaQqn3Q6UM8AbRbbRvs{i:D2}", $"sEdTM1uX8pu2do5XvTnutH6Hsou{i:D3}",
        "static", "Starts", DateTimeOffset.UnixEpoch, "cpu");

    private IRenderedComponent<QrExportDialog> Open(params SessionResult[] rows)
    {
        Services.AddSingleton<TimeProvider>(_time);
        return Render<QrExportDialog>(p => p.Add(x => x.Rows, rows));
    }

    [Fact]
    public void Nothing_Secret_Is_Drawn_Until_The_Code_Is_Asked_For()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        Assert.Empty(cut.FindAll("svg.qr"));
        cut.Find("button.show-code").Click();
        Assert.Single(cut.FindAll("svg.qr"));
    }

    [Fact]
    public void One_Wallet_Offers_Both_Formats_And_Several_Offer_Only_The_Backup()
    {
        Assert.False(Open(R(1)).Find("input.format-seed").HasAttribute("disabled"));

        IRenderedComponent<QrExportDialog> many = Open(R(1), R(2));
        Assert.True(many.Find("input.format-seed").HasAttribute("disabled"));
        Assert.Contains("one wallet", many.Find(".format-note").TextContent);
    }

    [Fact]
    public void The_Backup_Code_Carries_The_Prefix_The_Wallet_Looks_For()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();

        Assert.StartsWith("TNA11B", cut.FindComponent<QrCode>().Instance.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Seed_Format_Encodes_The_Seed_And_Nothing_Around_It()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.format-seed").Change(true);
        cut.Find("button.show-code").Click();

        Assert.Equal(R(1).Seed, cut.FindComponent<QrCode>().Instance.Text);
    }

    [Fact]
    public void Switching_The_Format_Takes_The_Rendered_Code_Away()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();
        Assert.Single(cut.FindAll("svg.qr"));

        cut.Find("input.format-seed").Change(true);

        Assert.Empty(cut.FindAll("svg.qr"));
    }

    [Fact]
    public void A_Code_Without_A_Password_Says_What_That_Costs()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();

        Assert.Contains("not encrypted", cut.Find(".qr-warning").TextContent);
    }

    [Fact]
    public void Passwords_That_Disagree_Build_Nothing()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("input.password").Change("one");
        cut.Find("input.password-confirm").Change("other");
        cut.Find("button.show-code").Click();

        Assert.Empty(cut.FindAll("svg.qr"));
        Assert.Contains("do not match", cut.Find(".qr-error").TextContent);
    }

    [Fact]
    public void More_Wallets_Than_One_Code_Holds_Are_Refused_With_A_Number()
    {
        SessionResult[] rows = Enumerable.Range(1, 40).Select(R).ToArray();
        IRenderedComponent<QrExportDialog> cut = Open(rows);

        cut.Find("button.show-code").Click();

        Assert.Empty(cut.FindAll("svg.qr"));
        Assert.Contains("Too many wallets for one code", cut.Find(".qr-error").TextContent);
    }

    [Fact]
    public void The_Code_Hides_Itself_And_Can_Be_Brought_Back()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));
        cut.Find("button.show-code").Click();

        _time.Advance(TimeSpan.FromSeconds(60));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("svg.qr")));
        cut.Find("button.show-code").Click();
        Assert.Single(cut.FindAll("svg.qr"));
    }

    [Fact]
    public void Both_Store_Links_Are_Under_The_Code()
    {
        IRenderedComponent<QrExportDialog> cut = Open(R(1));

        Assert.Equal(
            "https://apps.apple.com/us/app/staticbit/id6745529426",
            cut.Find("a.store-ios").GetAttribute("href"));
        Assert.Equal(
            "https://play.google.com/store/apps/details?id=io.staticbit.wallet",
            cut.Find("a.store-android").GetAttribute("href"));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*QrExportDialogTests"`
Expected: a build failure, `error CS0246: the type or namespace name "QrExportDialog" could not be found`.

- [ ] **Step 3: Write the markup**

`src/XrplVanity.App.Core/Components/QrExportDialog.razor`:

```razor
@using XrplVanity.Core
@inject TimeProvider Clock

@if (Rows is { Count: > 0 })
{
  <div class="modal-backdrop"></div>
  <div class="modal-box card-box qr-box" role="dialog" aria-modal="true" aria-label="Show as QR code" tabindex="-1" @onkeydown="OnKeyDown" @ref="_box">
    <h3>Show as QR code</h3>

    <div class="formats">
      <label><input class="format-backup" type="radio" name="qr-format" checked="@(!_seedOnly)" @onchange="() => SetFormat(false)" /> StaticBit backup — @Rows.Count @(Rows.Count == 1 ? "wallet" : "wallets")</label>
      <label><input class="format-seed" type="radio" name="qr-format" checked="@_seedOnly" disabled="@(Rows.Count != 1)" @onchange="() => SetFormat(true)" /> Seed only</label>
    </div>
    <div class="format-note">
      @if (Rows.Count != 1)
      {
        <text>A bare seed carries one wallet, so that format needs a selection of exactly one.</text>
      }
      else if (_seedOnly)
      {
        <text>StaticBit does not import a bare seed — it reads this as an address and offers to send. Use it for a wallet that takes a family seed directly.</text>
      }
      else
      {
        <text>Scanned by StaticBit's restore screen.</text>
      }
    </div>

    @if (!_seedOnly)
    {
      <div class="password-row">
        <input class="password form-control" type="password" placeholder="password (optional)" @bind="_password" @bind:event="oninput" />
        <input class="password-confirm form-control" type="password" placeholder="repeat it" @bind="_passwordConfirm" @bind:event="oninput" />
      </div>
    }

    @if (_code is null)
    {
      <button class="btn-primary-mint show-code t" disabled="@_building" @onclick="ShowCode">@(_building ? "Building…" : "Show code")</button>
    }
    else
    {
      <QrCode Text="@_code" />
      @if (!_seedOnly && _password.Length == 0)
      {
        <div class="qr-warning">This code is not encrypted. Anyone who photographs it owns these wallets.</div>
      }
      <div class="qr-caption">Scan with StaticBit on your phone.</div>
    }

    @if (_error is not null) { <div class="qr-error">@_error</div> }

    <div class="stores">
      <a class="store-ios" href="https://apps.apple.com/us/app/staticbit/id6745529426">App Store</a>
      <a class="store-android" href="https://play.google.com/store/apps/details?id=io.staticbit.wallet">Google Play</a>
    </div>

    <div class="buttons">
      <button class="btn-secondary-dark close t" @ref="_close" @onclick="() => OnClose.InvokeAsync()">Close</button>
    </div>
  </div>
}
```

- [ ] **Step 4: Write the code-behind**

`src/XrplVanity.App.Core/Components/QrExportDialog.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Shows a selection of results as a QR code. The dialog builds nothing until asked: the format and
/// the password are settled first, and only then does a secret appear on screen.
/// </summary>
public partial class QrExportDialog : ComponentBase, IDisposable
{
    /// <summary>Long enough to line a phone camera up — the 15 s seed reveal is not.</summary>
    private const int HideSeconds = 60;

    private ElementReference _box;
    private ElementReference _close;
    private ITimer? _hideTimer;
    private string? _code;
    private string? _error;
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private bool _seedOnly;
    private bool _building;
    private bool _wasVisible;
    private volatile bool _disposed;

    /// <summary>The rows to encode. Null or empty keeps the dialog closed.</summary>
    [Parameter] public IReadOnlyList<SessionResult>? Rows { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    protected override void OnParametersSet()
    {
        if (Rows is { Count: > 0 })
        {
            return;
        }

        // Closed: nothing about the last set may survive into the next one.
        Reset();
    }

    private void SetFormat(bool seedOnly)
    {
        _seedOnly = seedOnly;
        // The two formats are different secrets; a code left on screen would belong to the old one.
        DropCode();
    }

    private async Task ShowCode()
    {
        _error = null;
        if (Rows is not { Count: > 0 }) return;

        if (!_seedOnly && _password != _passwordConfirm)
        {
            _error = "The passwords do not match.";
            return;
        }

        if (_seedOnly)
        {
            SetCode(Rows[0].Seed);
            return;
        }

        _building = true;
        try
        {
            string? password = _password.Length == 0 ? null : _password;
            BackupWallet[] wallets = Rows
                .Select(r => new BackupWallet(r.Seed, r.Address, r.Pattern))
                .ToArray();

            // 600 000 PBKDF2 iterations is a visible pause on the render thread; the build runs off it.
            string code = await Task.Run(() => StaticBitBackup.ToQrText(wallets, password));
            SetCode(code);
        }
        catch (ArgumentException)
        {
            _error = "Nothing to encode.";
        }
        finally
        {
            _building = false;
        }
    }

    private void SetCode(string code)
    {
        // Base45 packs 5.5 bits per character; past this the symbol no longer fits one code.
        if (!_seedOnly && code.Length > MaxCodeCharacters)
        {
            _error = $"Too many wallets for one code. Select at most {Fitting()} of them.";
            return;
        }

        _code = code;
        _hideTimer?.Dispose();
        _hideTimer = Clock.CreateTimer(_ => Hide(), null, TimeSpan.FromSeconds(HideSeconds), Timeout.InfiniteTimeSpan);
    }

    /// <summary>1234 container bytes become 1851 or 1852 characters; the cap is the character count.</summary>
    private const int MaxCodeCharacters = 1852;

    /// <summary>How many of the selected rows would fit, found by building progressively smaller sets.</summary>
    private int Fitting()
    {
        if (Rows is null) return 0;

        string? password = _password.Length == 0 ? null : _password;
        for (int count = Rows.Count - 1; count >= 1; count--)
        {
            BackupWallet[] wallets = Rows.Take(count)
                .Select(r => new BackupWallet(r.Seed, r.Address, r.Pattern))
                .ToArray();
            if (StaticBitBackup.Pack(wallets, password).Length <= StaticBitBackup.MaxQrContainerBytes) return count;
        }

        return 0;
    }

    private void Hide()
    {
        if (_disposed) return;

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed) return;
                DropCode();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer is gone; nothing to refresh.
        }
        catch (InvalidOperationException)
        {
            // The component is no longer attached to a renderer.
        }
    }

    private void DropCode()
    {
        _code = null;
        _error = null;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }

    private void Reset()
    {
        DropCode();
        _password = string.Empty;
        _passwordConfirm = string.Empty;
        _seedOnly = false;
        _wasVisible = false;
    }

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnClose.InvokeAsync() : Task.CompletedTask;

    /// <summary>Focus lands on Close, the way the confirmation dialog focuses Cancel.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Rows is { Count: > 0 } && !_wasVisible)
        {
            _wasVisible = true;
            try
            {
                await _close.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                try
                {
                    await _box.FocusAsync();
                }
                catch (InvalidOperationException)
                {
                    // The dialog closed in the same batch; nothing to focus.
                }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }
}
```

- [ ] **Step 5: Write the styles**

`src/XrplVanity.App.Core/Components/QrExportDialog.razor.css`:

```css
.qr-box { max-width: 420px; text-align: center; }
.formats { display: flex; flex-direction: column; gap: 6px; text-align: left; margin-bottom: 6px; }
.formats label { display: flex; align-items: center; gap: 8px; color: var(--text); }
.format-note { color: var(--text-secondary); font-size: 12px; text-align: left; margin-bottom: 12px; }
.password-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 12px; }
.qr-warning { color: var(--yellow); font-size: 12px; margin-top: 10px; }
.qr-caption { color: var(--text-secondary); font-size: 12px; margin-top: 8px; }
.qr-error { color: var(--negative); font-size: 12px; margin-top: 10px; }
.stores { display: flex; gap: 14px; justify-content: center; margin-top: 12px; font-size: 12px; }
.stores a { color: var(--text-secondary); text-decoration: none; }
.stores a:hover { color: var(--mint); }
.buttons { display: flex; justify-content: flex-end; margin-top: 14px; }
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*QrExportDialogTests"`
Expected: `Failed: 0`.

If `More_Wallets_Than_One_Code_Holds_Are_Refused_With_A_Number` is slow, it is `Fitting()` packing repeatedly — that is acceptable here (no password in that test, so no PBKDF2 runs).

- [ ] **Step 7: Commit**

```bash
git add src/XrplVanity.App.Core/Components/QrExportDialog.razor src/XrplVanity.App.Core/Components/QrExportDialog.razor.cs src/XrplVanity.App.Core/Components/QrExportDialog.razor.css tests/XrplVanity.App.Tests/QrExportDialogTests.cs
git commit -m "feat(app): a dialog that shows found wallets as a QR code"
```

---

### Task 5: The two entry points

**Files:**
- Modify: `src/XrplVanity.App.Core/Components/ResultsList.razor` (row button, selection-bar button), `ResultsList.razor.cs` (the parameter), `ResultsList.razor.css` (one more column)
- Modify: `src/XrplVanity.App.Core/Pages/SearchPage.razor`, `SearchPage.razor.cs`
- Modify: `src/XrplVanity.App.Core/Pages/HistoryPage.razor`, `HistoryPage.razor.cs`
- Test: `tests/XrplVanity.App.Tests/ResultsListTests.cs`, `tests/XrplVanity.App.Tests/SearchPageTests.cs`

**Interfaces:**
- Consumes: `QrExportDialog` (Task 4).
- Produces: `ResultsList` gains `[Parameter] public EventCallback<IReadOnlyList<SessionResult>> OnShowQr { get; set; }`. Both buttons appear only when it has a delegate.

- [ ] **Step 1: Write the failing tests**

Add to `tests/XrplVanity.App.Tests/ResultsListTests.cs`:

```csharp
    [Fact]
    public void A_Row_Asks_The_Host_To_Show_Its_Own_Wallet()
    {
        RegisterServices();
        List<SessionResult>? asked = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1), R(2) })
            .Add(x => x.OnShowQr, (IReadOnlyList<SessionResult> rows) => asked = rows.ToList()));

        cut.FindAll("button.show-qr")[1].Click();

        Assert.Equal("rAddr2", Assert.Single(asked!).Address);
    }

    [Fact]
    public void The_Selection_Bar_Asks_For_Every_Selected_Row()
    {
        RegisterServices();
        List<SessionResult>? asked = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1), R(2) })
            .Add(x => x.Selectable, true)
            .Add(x => x.OnShowQr, (IReadOnlyList<SessionResult> rows) => asked = rows.ToList()));

        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[1].Change(true);
        cut.Find("button.show-qr-selected").Click();

        Assert.Equal(2, asked!.Count);
    }

    [Fact]
    public void Without_A_Qr_Callback_Neither_Button_Is_Rendered()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.Selectable, true));

        Assert.Empty(cut.FindAll("button.show-qr"));
        Assert.Empty(cut.FindAll("button.show-qr-selected"));
    }
```

Add to `tests/XrplVanity.App.Tests/HistoryPageTests.cs`. The page-level test lives here and not in
`SearchPageTests` because History's rows come from `MemoryResultStore`, which the tests already
fill; the Search page's rows come from a running search and there is no seam for them. Do not add a
test-only method to `SearchViewModel` to make one.

```csharp
    [Fact]
    public void The_Qr_Dialog_Opens_From_A_Row_And_Makes_The_Page_Inert()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("button.show-qr")[0].Click();

        Assert.Single(cut.FindAll(".qr-box"));
        Assert.True(cut.Find("header.page-head").HasAttribute("inert"));
        Assert.True(cut.Find(".content-scroll").HasAttribute("inert"));

        cut.Find("button.close").Click();

        Assert.Empty(cut.FindAll(".qr-box"));
        Assert.False(cut.Find("header.page-head").HasAttribute("inert"));
    }

    [Fact]
    public void The_Selection_Bar_Opens_The_Dialog_With_Every_Selected_Row()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[1].Change(true);
        cut.Find("button.show-qr-selected").Click();

        // Two wallets: the backup format is the only one a single code can carry.
        Assert.True(cut.Find("input.format-seed").HasAttribute("disabled"));
    }
```

`RenderPage` registers `TimeProvider.System`; `QrExportDialog` takes `TimeProvider` from the same
container, so these two tests need no change to it.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*ResultsListTests"`
Expected: a build failure, `error CS1061: "ResultsList" does not contain a definition for "OnShowQr"`.

- [ ] **Step 3: Add the parameter and the buttons**

In `ResultsList.razor.cs`, beside `OnDeleteSelected`:

```csharp
    /// <summary>
    /// Asked to show these rows as a QR code. The dialog belongs to the page: everything inside
    /// .content-scroll is covered by the inert attribute the page sets, so a dialog rendered from
    /// here would go inert along with the page behind it.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<SessionResult>> OnShowQr { get; set; }
```

In `ResultsList.razor`, inside the row, after the `copy-seed` button:

```razor
          @if (OnShowQr.HasDelegate) { <button class="btn-icon show-qr" title="Show as QR code" @onclick="() => OnShowQr.InvokeAsync(new[] { item })">▣</button> }
```

and in the selection bar's `.acts`, before `Delete selected`:

```razor
          @if (OnShowQr.HasDelegate)
          {
            <button class="btn-secondary-dark show-qr-selected t" @onclick="() => OnShowQr.InvokeAsync(SelectedItems())">Show QR…</button>
          }
```

In `ResultsList.razor.css`, the row grows one 28px column in every variant. Replace the five `--cols` rules and the media query with:

```css
.result-row { --cols: minmax(280px, 1.2fr) 28px 1fr auto minmax(120px, 0.8fr) 28px 28px 28px; display: grid; grid-template-columns: var(--cols); gap: 8px; align-items: center; height: 44px; border-top: 1px solid var(--dark-gray); }
.result-row.has-target { --cols: minmax(280px, 1.2fr) 28px 1fr auto auto minmax(120px, 0.8fr) 28px 28px 28px; }
.result-row.has-delete { --cols: minmax(280px, 1.2fr) 28px 1fr auto minmax(120px, 0.8fr) 28px 28px 28px 28px; }
.result-row.has-target.has-delete { --cols: minmax(280px, 1.2fr) 28px 1fr auto auto minmax(120px, 0.8fr) 28px 28px 28px 28px; }
```

```css
@media (max-width: 1000px) {
  .result-row, .result-row.has-target { --cols: minmax(230px, 1fr) 28px 120px 120px 28px 28px 28px; }
  .result-row.has-delete, .result-row.has-target.has-delete { --cols: minmax(230px, 1fr) 28px 120px 120px 28px 28px 28px 28px; }
  .result-row .target, .result-row .time { display: none; }
}
```

> The QR button is rendered only when `OnShowQr` has a delegate, but these columns are
> unconditional. That is the existing shape of this file — `has-delete` is a class, not a count —
> so if a host ever uses `ResultsList` without the callback the row will carry one empty 28px
> column. Both hosts pass it, so do not add a sixth variant for a case nothing reaches.

- [ ] **Step 4: Host the dialog on both pages**

`SearchPage.razor` — pass the callback and render the dialog, and widen the `inert` condition:

```razor
<header class="page-head" inert="@(_clearPending || _qrRows is not null)">
```
```razor
<div class="content-scroll" inert="@(_clearPending || _qrRows is not null)">
```
```razor
      <ResultsList Items="Vm.Results" ShowTarget="true" OnShowQr="rows => _qrRows = rows"
                   RevealSeconds="Vm.Settings.SeedRevealSeconds" ClipboardClearSeconds="Vm.Settings.ClipboardClearSeconds" />
```
```razor
<QrExportDialog Rows="_qrRows" OnClose="() => _qrRows = null" />
```

`SearchPage.razor.cs` — one field beside `_clearPending`:

```csharp
    private IReadOnlyList<ViewModels.SessionResult>? _qrRows;
```

`HistoryPage.razor` — the same three edits, with its own dialog condition:

```razor
<header class="page-head" inert="@(_pending is not null || _qrRows is not null)">
```
```razor
<div class="content-scroll" inert="@(_pending is not null || _qrRows is not null)">
```
```razor
  <ResultsList Items="Filtered()" ShowTarget="true" Selectable="true" KnownAddresses="_knownAddresses"
               OnDelete="DeleteCallback" OnDeleteSelected="DeleteSelectedCallback" OnShowQr="rows => _qrRows = rows"
               RevealSeconds="Vm.Settings.SeedRevealSeconds" ClipboardClearSeconds="Vm.Settings.ClipboardClearSeconds" />
```
```razor
<QrExportDialog Rows="_qrRows" OnClose="() => _qrRows = null" />
```

`HistoryPage.razor.cs` — one field beside `_pending`:

```csharp
    private IReadOnlyList<SessionResult>? _qrRows;
```

- [ ] **Step 5: Run the whole application suite**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release`
Expected: `Failed: 0`. The existing `SearchPageTests` and `HistoryPageTests` must still pass — the widened `inert` conditions are the likeliest thing to have broken them.

- [ ] **Step 6: Build the solution warning-free**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: `Сборка успешно завершена.` with no warning lines.

- [ ] **Step 7: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): a row and a selection can be shown as a QR code"
```

---

### Task 6: Documentation

**Files:**
- Modify: `docs/desktop-app.md`

- [ ] **Step 1: Add the feature to the Overview**

In the Search bullet, after the paragraph about saving and loading a pattern list, add:

```markdown
  A found wallet can also leave as a QR code rather than as a file. The button in a result row
  shows that one wallet; `Show QR…` in the selection bar shows every selected row. The dialog
  offers two payloads: the StaticBit backup container, which the StaticBit wallet restores from by
  scanning, and the bare `sEd…` seed, which it does not import and which is offered only for a
  single wallet. A password is optional and encrypts the container; without one the code is not
  encrypted and says so. One code holds 22 wallets, or 21 with a password. Nothing is written to
  disk — the code is inline SVG, is never an image file, and hides itself after 60 seconds.
```

- [ ] **Step 2: Add QRCoder to the third-party table**

In the "Fonts and third-party assets" table, add a row. Read the version out of the
`PackageReference` that Task 3, Step 1 wrote into
`src/XrplVanity.App.Core/XrplVanity.App.Core.csproj` and put that exact string in the second
column — the table records what the build actually restores:

```markdown
| [QRCoder](https://github.com/codebude/QRCoder) | v1.6.0 | MIT — used for the QR module matrix only; the code is drawn as inline SVG by this application |
```

- [ ] **Step 3: Add the smoke checklist entries**

After the last numbered item:

```markdown
24. **A wallet crosses to the phone as a QR code.** With at least one result on screen, press the
    QR button in its row. The dialog opens, the page behind it does not answer clicks, and no code
    is drawn yet. Press **Show code**: a QR code appears on a white plate with the warning that it
    is not encrypted, the caption "Scan with StaticBit on your phone." and the two store links.
    Right-click the code: the menu offers no "Save image as…". Wait 60 s — the code disappears and
    **Show code** comes back.
25. **The code really imports.** Copy the code's text out of the debugger (or re-run the build with
    a temporary trace) and paste it into `tools/backup-decoder/index.html` from the wallet
    repository: it decodes and lists the addresses. Then scan the on-screen code with StaticBit's
    restore screen on a phone and confirm the wallets it offers are exactly the rows that were
    selected, with the matched pattern as each wallet's label. Repeat once with a password.
26. **A selection that does not fit is refused.** Select forty rows on History and press
    **Show QR…**: pressing **Show code** draws nothing and the dialog says how many of them would
    fit. Reduce the selection to that number and the code appears.
```

- [ ] **Step 4: Commit**

```bash
git add docs/desktop-app.md
git commit -m "docs: the QR export, its two payloads and its smoke checks"
```

---

## Self-review notes for the executor

- The design document's capacity table (22 / 21 wallets) and Task 2's capacity test must agree. If
  the test measures something else, change **both**, and say so in the commit message.
- Task 5's page-level tests live in `HistoryPageTests`, which is the only page whose rows a test can
  fill. `SearchPageTests` gains nothing here; the Search page's wiring is the same two lines and is
  covered by the solution building and by smoke check 24.
- Task 6's QRCoder row says v1.6.0. If `dotnet add package` resolved something else in Task 3, the
  `.csproj` is right and the table follows it.
- `QRCoder`'s `QRCodeData.ModuleMatrix` may or may not already carry a quiet zone. Task 3 does not
  depend on the answer: the white plate's padding is the quiet zone either way, and an extra one
  costs nothing but a slightly larger margin.
