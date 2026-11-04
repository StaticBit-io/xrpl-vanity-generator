# GPU Backend — Plan B (M5–M6: integration and speed) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the proven GPU derivation pipeline into a working `--gpu` vanity search — device-side matching with semantics identical to the CPU, a hit buffer, a host gate that prints only CPU-reproduced hits — and then make it ~5x faster with windowed fixed-base scalar multiplication, re-verified against every existing anchor.

**Architecture:** The host builds the target set with the same `PatternSet.Builder` the CPU path uses, flattens the expanded targets into sorted per-(position, length) groups and uploads them once; the search kernel derives each candidate's address from a per-launch PRF, matches it on the device by binary search / substring scan, and appends hits to a fixed 4,096-slot buffer behind an atomic counter. `GpuSearchEngine` drains the buffer after every launch and re-derives each hit with `CandidateEvaluator` (the CPU reference) — only hits the CPU reproduces are yielded. The CLI gains `--gpu`; output, `--count`, `--out`, warnings and exit codes are shared with the CPU path through a small `MatchSink`. M6 adds a 256-entry base-point table built on the device by the already-verified naive scalar multiplication and a windowed `scalarmult_base_windowed` (libsodium `ge25519_scalarmult_base` layout), proven equal to the naive path and to BouncyCastle before it replaces the naive path in the pipeline.

**Tech Stack:** .NET 10, C# latest, CUDA driver API via P/Invoke (Plan A layer), NVRTC-generated PTX (compute_75), xUnit v3 4.0.0, BouncyCastle.Cryptography 2.7.0 (via Core).

**Spec:** `docs/2026-09-03-xrpl-vanity-gpu-design.md` (sections "Device-side matching", "Hit reporting and the re-verification gate", "Host (.NET) side", milestone M5–M6) and, for the CPU reference, `docs/2026-09-03-xrpl-vanity-design.md`.

## Global Constraints

- Target `net10.0`; `<Nullable>enable</Nullable>`; `<LangVersion>latest</LangVersion>`. `XrplVanity.Gpu` keeps `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, `<ImplicitUsings>disable</ImplicitUsings>`, and no package dependencies beyond `XrplVanity.Core`.
- `dotnet build` must NEVER require CUDA. PTX target `compute_75`; regenerate with `python gpu/build-ptx.py` after every `.cu` change and commit `.cu` + `.ptx` together.
- Kernel file `gpu/kernels/xrpl_vanity.cu` grows by appending sections; existing sections are modified ONLY where a task says so explicitly (Task 3 refactors `k_prf_entropy` to call a shared `prf_entropy`; Task 6 adds a `table` parameter to `derive_address`, `k_derive_address` and `k_search`).
- Every field element produced by a device function is canonical (`< p`).
- Match semantics on the device must equal `PatternSet.TryMatch` as a predicate (hit / no hit) for every address; the host re-verification decides the final `Match` (address, seed, pattern, position) with the CPU code.
- **Every printed GPU result must have been re-derived by the CPU reference** (`CandidateEvaluator.TryEvaluate` succeeds AND its address equals the GPU address). A disagreement is counted and reported to stderr, never printed as a result.
- Hit buffer: `4096` slots per launch; each slot = 16 entropy bytes + the 36-byte address record (52 bytes); overflow is counted and reported, never silent.
- `--gpu` with no CUDA driver/device prints a clear error and exits `2` — it never silently falls back to the CPU. Exit codes otherwise unchanged: `0` when `--count` is satisfied, `2` usage error, `130` interrupted.
- Windowed scalar multiplication requires a **clamped** scalar (bit 255 clear); every scalar in the pipeline is clamped, and tests only feed clamped scalars.
- Tests: xUnit v3 4.0.0, built-in `Assert`, NO FluentAssertions. GPU tests use `GpuTestHelper.RequireContext()` (skips without CUDA / pre-Turing). **Run tests with `dotnet run`, never `dotnet test`**: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"`; whole suite by omitting `-class`. Build warning-free.
- The two-BouncyCastle namespace collision in the test project is real: any test that names an `Org.BouncyCastle.*` type directly must use `extern alias BcCrypto;` + `BcCrypto::Org.BouncyCastle...` (the csproj alias already exists). Tests in this plan avoid direct BouncyCastle types except where stated.
- Commit locally after each task with a plain Conventional Commit; NO `Co-Authored-By` / attribution trailers; do NOT push or publish. All artifacts in English.
- Constants reused from Plan A (already in the `.cu`): `D2_LIMBS`, `BX_LIMBS`, `BY_LIMBS`; XRPL alphabet `rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz`; known vector `sEdTuRgwYiEcubPJfRJUArVyn7AXkVj → rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK`; RFC 8032 TEST 1–3 seeds/public keys as listed in Plan A.

## File Structure

```
src/XrplVanity.Core/PatternSet.cs        # + EnumerateTargets() (Task 1)
src/XrplVanity.Gpu/TargetTable.cs        # flatten/sort/group targets for upload (Task 1)
src/XrplVanity.Gpu/GpuSearchEngine.cs    # launches, hit drain, CPU re-verification gate (Task 3; table in Task 6)
src/XrplVanity.Cli/CliOptions.cs         # + --gpu (Task 4)
src/XrplVanity.Cli/MatchSink.cs          # shared stdout/--out/--count reporting (Task 4)
src/XrplVanity.Cli/Program.cs            # RunCpu / RunGpu split (Task 4)
gpu/kernels/xrpl_vanity.cu               # M5 matcher + search kernel (Tasks 2–3), M6 windowed (Tasks 5–6)
gpu/kernels/xrpl_vanity.ptx              # regenerated per kernel task
README.md                                # --gpu documented (Task 4), performance note (Task 6)
tests/XrplVanity.Tests/PatternSetTests.cs            # + EnumerateTargets test (Task 1)
tests/XrplVanity.Tests/Gpu/TargetTableTests.cs        # host-only (Task 1)
tests/XrplVanity.Tests/Gpu/GpuMatcherTests.cs         # device predicate == CPU predicate (Task 2)
tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs          # engine: verified hits, overflow, empty set; benchmark (Tasks 3, 6)
tests/XrplVanity.Tests/CliOptionsTests.cs             # + --gpu (Task 4)
tests/XrplVanity.Tests/MatchSinkTests.cs              # (Task 4)
tests/XrplVanity.Tests/Gpu/GpuCliTests.cs             # headless --gpu search (Task 4)
tests/XrplVanity.Tests/Gpu/GpuWindowedTests.cs        # windowed == naive == RFC/BC; table anchor (Task 5)
tests/XrplVanity.Tests/Gpu/GpuPipelineTests.cs        # table-aware RunDerive, 1M re-run (Task 6)
```

---

### Task 1: Expanded-target accessor in Core and the upload table

**Files:**
- Modify: `src/XrplVanity.Core/PatternSet.cs` (add `EnumerateTargets()` after `HasPatterns`)
- Create: `src/XrplVanity.Gpu/TargetTable.cs`
- Test: `tests/XrplVanity.Tests/PatternSetTests.cs` (add one test), `tests/XrplVanity.Tests/Gpu/TargetTableTests.cs`

**Interfaces:**
- Consumes: `PatternSet` internals (`_starts`/`_ends` buckets expose `Values`, `_contains`), `MatchPosition`.
- Produces:
  - `IEnumerable<(MatchPosition Position, string Value)> PatternSet.EnumerateTargets()` — every expanded target exactly as the matcher checks it.
  - `sealed class XrplVanity.Gpu.TargetTable` — `static TargetTable Build(PatternSet patterns)`; `uint[] Groups` (4 uints per group: `position` 0=starts/1=ends/2=contains, `length`, `charOffset`, `count`); `byte[] Chars` (ASCII, groups contiguous, targets within a group sorted ordinal and packed at stride `length`); `int GroupCount`; constants `PositionStarts = 0`, `PositionEnds = 1`, `PositionContains = 2`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/XrplVanity.Tests/PatternSetTests.cs` inside the class:

```csharp
    [Fact]
    public void EnumerateTargets_Yields_Exactly_The_Expanded_Set()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "ab", ignoreCase: true)      // ab aB Ab AB
            .Add(MatchPosition.Ends, "z", ignoreCase: true)         // z Z
            .Add(MatchPosition.Contains, "q", ignoreCase: false)    // q
            .Build();

        List<(MatchPosition Position, string Value)> targets = set.EnumerateTargets().ToList();

        Assert.Equal(7, targets.Count);
        Assert.Equal(new[] { "AB", "Ab", "aB", "ab" },
            targets.Where(t => t.Position == MatchPosition.Starts).Select(t => t.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(new[] { "Z", "z" },
            targets.Where(t => t.Position == MatchPosition.Ends).Select(t => t.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(new[] { "q" },
            targets.Where(t => t.Position == MatchPosition.Contains).Select(t => t.Value));
    }
```

Add `using System.Collections.Generic;` and `using System.Linq;` to that file's usings if absent.

`tests/XrplVanity.Tests/Gpu/TargetTableTests.cs`:

```csharp
using System.Text;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class TargetTableTests
{
    [Fact]
    public void Groups_Are_Sorted_By_Position_And_Length_With_Ordinal_Sorted_Targets()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "q", ignoreCase: false)
            .Add(MatchPosition.Ends, "z", ignoreCase: true)
            .Add(MatchPosition.Starts, "ab", ignoreCase: true)
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(3, table.GroupCount);
        // group 0: starts, length 2, 4 targets sorted ordinal: AB Ab aB ab
        Assert.Equal(new uint[] { TargetTable.PositionStarts, 2, 0, 4 }, table.Groups[..4]);
        // group 1: ends, length 1, 2 targets: Z z
        Assert.Equal(new uint[] { TargetTable.PositionEnds, 1, 8, 2 }, table.Groups[4..8]);
        // group 2: contains, length 1, 1 target: q
        Assert.Equal(new uint[] { TargetTable.PositionContains, 1, 10, 1 }, table.Groups[8..12]);
        Assert.Equal("ABAbaBabZzq", Encoding.ASCII.GetString(table.Chars));
    }

    [Fact]
    public void Duplicate_Targets_Are_Stored_Once()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "ab", ignoreCase: false)
            .Add(MatchPosition.Contains, "ab", ignoreCase: false)
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(1, table.GroupCount);
        Assert.Equal(1u, table.Groups[3]);
        Assert.Equal("ab", Encoding.ASCII.GetString(table.Chars));
    }

    [Fact]
    public void Empty_Set_Has_No_Groups()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "bit@coin", ignoreCase: true) // unreachable, dropped
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(0, table.GroupCount);
        Assert.Empty(table.Chars);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*TargetTableTests"`
Expected: build error — `EnumerateTargets` and `TargetTable` do not exist.

- [ ] **Step 3: Implement**

In `src/XrplVanity.Core/PatternSet.cs`, after the `HasPatterns` property, add:

```csharp
    /// <summary>
    /// Every expanded target string with its position — exactly the set <see cref="TryMatch"/>
    /// checks. Consumers that match elsewhere (the GPU) build their structures from this so the
    /// Builder stays the single source of truth for normalization and case expansion.
    /// </summary>
    public IEnumerable<(MatchPosition Position, string Value)> EnumerateTargets()
    {
        foreach (Bucket bucket in _starts)
        {
            foreach (string value in bucket.Values)
            {
                yield return (MatchPosition.Starts, value);
            }
        }

        foreach (Bucket bucket in _ends)
        {
            foreach (string value in bucket.Values)
            {
                yield return (MatchPosition.Ends, value);
            }
        }

        foreach (string value in _contains)
        {
            yield return (MatchPosition.Contains, value);
        }
    }
```

`src/XrplVanity.Gpu/TargetTable.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// The expanded targets of a <see cref="PatternSet"/> flattened for upload: groups keyed by
/// (position, length) in ascending order, each group's targets sorted ordinal and packed at a
/// fixed stride equal to their length, so the device can binary-search a group.
/// </summary>
public sealed class TargetTable
{
    public const uint PositionStarts = 0;
    public const uint PositionEnds = 1;
    public const uint PositionContains = 2;

    private TargetTable(uint[] groups, byte[] chars)
    {
        Groups = groups;
        Chars = chars;
    }

    /// <summary>Four values per group: position, length, offset into <see cref="Chars"/>, count.</summary>
    public uint[] Groups { get; }

    public byte[] Chars { get; }

    public int GroupCount => Groups.Length / 4;

    public static TargetTable Build(PatternSet patterns)
    {
        SortedDictionary<(uint Position, int Length), HashSet<string>> grouped = new();
        foreach ((MatchPosition position, string value) in patterns.EnumerateTargets())
        {
            uint code = position switch
            {
                MatchPosition.Starts => PositionStarts,
                MatchPosition.Ends => PositionEnds,
                _ => PositionContains,
            };
            (uint, int) key = (code, value.Length);
            if (!grouped.TryGetValue(key, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                grouped[key] = set;
            }

            set.Add(value);
        }

        List<uint> groups = new(grouped.Count * 4);
        List<byte> chars = new();
        foreach (KeyValuePair<(uint Position, int Length), HashSet<string>> entry in grouped)
        {
            List<string> sorted = entry.Value.OrderBy(v => v, StringComparer.Ordinal).ToList();
            groups.Add(entry.Key.Position);
            groups.Add((uint)entry.Key.Length);
            groups.Add((uint)chars.Count);
            groups.Add((uint)sorted.Count);
            foreach (string value in sorted)
            {
                chars.AddRange(Encoding.ASCII.GetBytes(value));
            }
        }

        return new TargetTable(groups.ToArray(), chars.ToArray());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*TargetTableTests"` and `-class "*PatternSetTests"`.
Expected: PASS (3 + all PatternSet tests). Whole suite green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core/PatternSet.cs src/XrplVanity.Gpu/TargetTable.cs tests/XrplVanity.Tests
git commit -m "feat(gpu): expose expanded pattern targets and flatten them for device upload"
```

---

### Task 2: Device-side matcher, proven equal to `PatternSet.TryMatch`

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the matcher section), regenerate PTX
- Test: `tests/XrplVanity.Tests/Gpu/GpuMatcherTests.cs`

**Interfaces:**
- Consumes: `TargetTable`, `CudaContext`, `GpuTestHelper`, `BouncyCastleDeriver`, `PatternSet.TryMatch`.
- Produces (device): `cmp_bytes`, `group_has` (binary search inside a group), `matches_any(const u8* body, int bodyLen, const u32* groups, int groupCount, const u8* chars) -> bool`; test entry `k_match_records(const u8* records, int n, const u32* groups, int groupCount, const u8* chars, u8* flags)` over 36-byte address records (`[len][chars…]`, chars[0] = 'r').

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuMatcherTests.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Text;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuMatcherTests
{
    private static byte[] Records(string[] addresses)
    {
        byte[] records = new byte[addresses.Length * 36];
        for (int i = 0; i < addresses.Length; i++)
        {
            records[i * 36] = (byte)addresses[i].Length;
            Encoding.ASCII.GetBytes(addresses[i], 0, addresses[i].Length, records, i * 36 + 1);
        }

        return records;
    }

    private static byte[] MatchOnDevice(byte[] records, int n, TargetTable table)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_match_records");

        using DeviceBuffer drec = context.Alloc(records.Length);
        using DeviceBuffer dgroups = context.Alloc(Math.Max(4, table.Groups.Length * 4));
        using DeviceBuffer dchars = context.Alloc(Math.Max(1, table.Chars.Length));
        using DeviceBuffer dflags = context.Alloc(n);
        drec.CopyFrom(records);
        if (table.Groups.Length > 0)
        {
            dgroups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        }

        if (table.Chars.Length > 0)
        {
            dchars.CopyFrom(table.Chars);
        }

        context.Launch(fn, (n + 255) / 256, 256, drec, n, dgroups, table.GroupCount, dchars, dflags);
        context.Synchronize();

        byte[] flags = new byte[n];
        dflags.CopyTo(flags);
        return flags;
    }

    private static string[] DeriveAddresses(int count, int seed)
    {
        Random rng = new(seed);
        BouncyCastleDeriver deriver = new();
        Span<char> buffer = stackalloc char[48];
        byte[] entropy = new byte[16];
        string[] addresses = new string[count];
        for (int i = 0; i < count; i++)
        {
            rng.NextBytes(entropy);
            int len = deriver.DeriveAddress(entropy, buffer);
            addresses[i] = new string(buffer[..len]);
        }

        return addresses;
    }

    [Fact]
    public void Device_Predicate_Equals_Cpu_TryMatch_Over_Real_Addresses()
    {
        // Short patterns so every position gets plenty of hits among 20,000 real addresses.
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: true)
            .Add(MatchPosition.Starts, "pQ", ignoreCase: false)
            .Add(MatchPosition.Ends, "z", ignoreCase: true)
            .Add(MatchPosition.Ends, "9", ignoreCase: false)
            .Add(MatchPosition.Contains, "ab", ignoreCase: true)
            .Add(MatchPosition.Contains, "xyz", ignoreCase: false)
            .Build();
        string[] addresses = DeriveAddresses(20_000, 20260903);

        byte[] gpu = MatchOnDevice(Records(addresses), addresses.Length, TargetTable.Build(set));

        int hits = 0;
        for (int i = 0; i < addresses.Length; i++)
        {
            bool cpu = set.TryMatch(addresses[i], out _, out _);
            Assert.True(cpu == (gpu[i] == 1), $"index {i} {addresses[i]}: cpu={cpu} gpu={gpu[i]}");
            if (cpu)
            {
                hits++;
            }
        }

        Assert.True(hits > 100, $"only {hits} hits; the pattern set should hit often"); // sanity: the test exercised real matches
    }

    [Fact]
    public void Each_Position_Is_Checked_Independently()
    {
        string[] addresses =
        {
            "rABCdefghijkmnpqstuvwxyz23456789ABC", // starts "ABC", ends "ABC", contains "ABC"
            "rZZZdefghijkmnpqstuvwxyz23456789ZZZ",
            "rQQQdefghijkmnpqstuvwxyz23456789ZZZ",
        };
        byte[] records = Records(addresses);

        byte[] starts = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Starts, "ABC", ignoreCase: false).Build()));
        byte[] ends = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Ends, "ZZZ", ignoreCase: false).Build()));
        byte[] contains = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Contains, "hijk", ignoreCase: false).Build()));

        Assert.Equal(new byte[] { 1, 0, 0 }, starts);
        Assert.Equal(new byte[] { 0, 1, 1 }, ends);
        Assert.Equal(new byte[] { 1, 1, 1 }, contains);
    }

    [Fact]
    public void Empty_Table_Matches_Nothing()
    {
        string[] addresses = DeriveAddresses(64, 7);
        TargetTable empty = TargetTable.Build(new PatternSet.Builder().Build());

        byte[] gpu = MatchOnDevice(Records(addresses), addresses.Length, empty);

        Assert.All(gpu, f => Assert.Equal(0, f));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"`
Expected: FAIL — `cuModuleGetFunction(k_match_records)` named symbol not found.

- [ ] **Step 3: Append the matcher section and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
// ---------------------------------------------------------------------------
// M5: device-side matching over host-sorted target groups.
// groups: 4 x u32 per group = { position (0 starts, 1 ends, 2 contains), length,
//         offset into chars, count }. Targets inside a group are sorted ordinal
//         (unsigned byte order) and packed at stride = length.
// The predicate below equals XrplVanity.Core.PatternSet.TryMatch's hit / no-hit result.
// ---------------------------------------------------------------------------
__device__ __forceinline__ int cmp_bytes(const u8* a, const u8* b, int len)
{
    for (int i = 0; i < len; i++) {
        if (a[i] != b[i]) return (a[i] < b[i]) ? -1 : 1;
    }
    return 0;
}

// Binary search for `slice` (exactly `len` bytes) inside one sorted group.
__device__ bool group_has(const u8* chars, u32 offset, u32 count, u32 len, const u8* slice)
{
    int lo = 0;
    int hi = (int)count - 1;
    while (lo <= hi) {
        int mid = (lo + hi) >> 1;
        int c = cmp_bytes(chars + offset + (u32)mid * len, slice, (int)len);
        if (c == 0) return true;
        if (c < 0) lo = mid + 1; else hi = mid - 1;
    }
    return false;
}

// body = the address WITHOUT its leading 'r'; bodyLen = its length.
__device__ bool matches_any(const u8* body, int bodyLen, const u32* groups, int groupCount, const u8* chars)
{
    for (int g = 0; g < groupCount; g++) {
        u32 pos = groups[g * 4];
        u32 len = groups[g * 4 + 1];
        u32 off = groups[g * 4 + 2];
        u32 cnt = groups[g * 4 + 3];
        if ((int)len > bodyLen || cnt == 0 || len == 0) continue;
        if (pos == 0) {
            if (group_has(chars, off, cnt, len, body)) return true;
        } else if (pos == 1) {
            if (group_has(chars, off, cnt, len, body + bodyLen - (int)len)) return true;
        } else {
            for (int s = 0; s + (int)len <= bodyLen; s++) {
                if (group_has(chars, off, cnt, len, body + s)) return true;
            }
        }
    }
    return false;
}

// Test entry: n x 36-byte address records ([len][chars...]) -> flags[i] = 1 on a hit.
extern "C" __global__ void k_match_records(const u8* records, int n, const u32* groups, int groupCount, const u8* chars, u8* flags)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    const u8* rec = records + i * 36;
    int len = rec[0];
    flags[i] = (len >= 1 && matches_any(rec + 2, len - 1, groups, groupCount, chars)) ? 1 : 0;
}
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"`
Expected: PASS (3 tests; the 20,000-address predicate comparison must agree on every index). Whole suite green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): device-side pattern matching proven equal to the CPU predicate"
```

---

### Task 3: Search kernel, hit buffer, and `GpuSearchEngine` with the CPU re-verification gate

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (extract `prf_entropy`; append `k_search`), regenerate PTX
- Create: `src/XrplVanity.Gpu/GpuSearchEngine.cs`
- Test: `tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs`

**Interfaces:**
- Consumes: `matches_any`, `derive_address`, `sha512`; `TargetTable`; `CudaContext`/`DeviceBuffer` (incl. `Clear()`); `CandidateEvaluator`, `BouncyCastleDeriver`, `Match`.
- Produces:
  - device `prf_entropy(const u8* key32, u64 counter, u8* out16)`; `k_search(const u8* key32, u64 start, int n, const u32* groups, int groupCount, const u8* chars, u8* hits, u32* hitCount, int hitCapacity)` writing 52-byte hit records (entropy16 + record36).
  - `sealed class XrplVanity.Gpu.GpuSearchEngine : IDisposable` — `const int HitCapacity = 4096`, `const int CandidatesPerLaunch = 1 << 20`, `const int SubLaunchSize = 1 << 16`; ctor `(CudaContext context, PatternSet patterns)` (throws `ArgumentException` when the set has no targets); `string DeviceName`; `long CandidatesSearched`, `long ConsistencyErrors`, `long OverflowDrops`; `List<Match> RunLaunch()` — one launch of `CandidatesPerLaunch` candidates under a fresh key, returning only CPU-verified matches; `void Dispose()` (device buffers; the context is owned by the caller).

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuSearchTests
{
    [Fact]
    public void Finds_Cpu_Verified_Matches_Without_Overflow()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        // "abc" case-insensitive = 8 variants / 58^3 ~ 4e-5 per candidate -> ~40 hits per 2^20 candidates.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "abc", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(context, patterns);

        List<Match> matches = engine.RunLaunch();

        Assert.Equal(GpuSearchEngine.CandidatesPerLaunch, engine.CandidatesSearched);
        Assert.True(matches.Count > 0, "expected roughly 40 hits in 2^20 candidates");
        Assert.Equal(0, engine.ConsistencyErrors);
        Assert.Equal(0, engine.OverflowDrops);

        // The seed must re-derive to the address (the gate already checked this; pin it here too).
        BouncyCastleDeriver deriver = new();
        Span<char> buffer = stackalloc char[48];
        foreach (Match m in matches)
        {
            Assert.True(patterns.TryMatch(m.Address, out _, out _), $"{m.Address} does not match on the CPU");
            Assert.Equal(MatchPosition.Starts, m.Position);
            int len = deriver.DeriveAddress(Seed.DecodeEntropy(m.Seed), buffer);
            Assert.Equal(m.Address, new string(buffer[..len]));
        }
    }

    [Fact]
    public void Overflow_Is_Counted_And_Returned_Hits_Are_Capped()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        // "a" case-insensitive = 2 / 58 ~ 3.4% -> ~36,000 hits per launch, far above the 4,096 slots.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(context, patterns);

        List<Match> matches = engine.RunLaunch();

        Assert.Equal(GpuSearchEngine.HitCapacity, matches.Count);
        Assert.True(engine.OverflowDrops > 0, "overflow must be counted, not silent");
        Assert.Equal(0, engine.ConsistencyErrors);
    }

    [Fact]
    public void Each_Launch_Uses_Fresh_Entropy()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(context, patterns);

        List<Match> first = engine.RunLaunch();
        List<Match> second = engine.RunLaunch();

        Assert.True(first.Count > 0 && second.Count > 0);
        HashSet<string> seeds = new(StringComparer.Ordinal);
        foreach (Match m in first) seeds.Add(m.Seed);
        foreach (Match m in second)
        {
            Assert.True(seeds.Add(m.Seed), $"seed {m.Seed} repeated across launches");
        }

        Assert.Equal(2L * GpuSearchEngine.CandidatesPerLaunch, engine.CandidatesSearched);
    }

    [Fact]
    public void Rejects_A_Pattern_Set_With_No_Targets()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        PatternSet empty = new PatternSet.Builder().Add(MatchPosition.Starts, "bit@coin", ignoreCase: true).Build();

        Assert.Throws<ArgumentException>(() => new GpuSearchEngine(context, empty));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"`
Expected: build error — `GpuSearchEngine` does not exist.

- [ ] **Step 3: Kernel — extract `prf_entropy`, append `k_search`; regenerate PTX**

In `gpu/kernels/xrpl_vanity.cu`, REPLACE the existing `k_prf_entropy` kernel (the whole function, currently the last kernel in the M4 section) with:

```cpp
// Per-launch entropy PRF: out16 = SHA-512(key32 || LE64(counter))[0..16].
__device__ void prf_entropy(const u8* key32, u64 counter, u8* out16)
{
    u8 m[40];
    for (int j = 0; j < 32; j++) m[j] = key32[j];
    for (int j = 0; j < 8; j++) m[32 + j] = (u8)(counter >> (8 * j));
    u8 h[64];
    sha512(m, 40, h);
    for (int j = 0; j < 16; j++) out16[j] = h[j];
}

extern "C" __global__ void k_prf_entropy(const u8* key32, u64 start, u8* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    prf_entropy(key32, start + (u64)i, out + i * 16);
}
```

Then APPEND after the M5 matcher section:

```cpp
// ---------------------------------------------------------------------------
// M5: search kernel. Each thread derives one candidate from the launch key and
// its counter, matches it on the device, and appends a 52-byte hit record
// (entropy16 || address record36) behind an atomic counter. Slots beyond
// hitCapacity are dropped; the host reads hitCount to detect the overflow.
// ---------------------------------------------------------------------------
extern "C" __global__ void k_search(const u8* key32, u64 start, int n,
                                    const u32* groups, int groupCount, const u8* chars,
                                    u8* hits, u32* hitCount, int hitCapacity)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    u8 e[16];
    prf_entropy(key32, start + (u64)i, e);
    u8 rec[36];
    derive_address(e, rec);
    int len = rec[0];
    if (len >= 1 && matches_any(rec + 2, len - 1, groups, groupCount, chars)) {
        u32 slot = atomicAdd(hitCount, 1u);
        if ((int)slot < hitCapacity) {
            u8* h = hits + (u32)slot * 52u;
            for (int j = 0; j < 16; j++) h[j] = e[j];
            for (int j = 0; j < 36; j++) h[16 + j] = rec[j];
        }
    }
}
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Host — `GpuSearchEngine`**

`src/XrplVanity.Gpu/GpuSearchEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// Runs the device search and re-verifies every device hit with the CPU reference before
/// yielding it. Not thread-safe; one engine per context/thread.
/// </summary>
public sealed class GpuSearchEngine : IDisposable
{
    public const int HitCapacity = 4096;
    public const int CandidatesPerLaunch = 1 << 20;
    public const int SubLaunchSize = 1 << 16;
    private const int HitRecordSize = 52;
    private const int BlockSize = 128;

    private readonly CudaContext _context;
    private readonly CudaFunction _search;
    private readonly DeviceBuffer _key;
    private readonly DeviceBuffer _groups;
    private readonly DeviceBuffer _chars;
    private readonly DeviceBuffer _hits;
    private readonly DeviceBuffer _hitCount;
    private readonly int _groupCount;
    private readonly CandidateEvaluator _verifier;
    private ulong _counter;
    private bool _disposed;

    public GpuSearchEngine(CudaContext context, PatternSet patterns)
    {
        _context = context;
        TargetTable table = TargetTable.Build(patterns);
        if (table.GroupCount == 0)
        {
            throw new ArgumentException("The pattern set has no reachable targets to search for.", nameof(patterns));
        }

        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        _search = module.GetFunction("k_search");
        _groupCount = table.GroupCount;

        _groups = context.Alloc(table.Groups.Length * 4);
        _groups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        _chars = context.Alloc(Math.Max(1, table.Chars.Length));
        if (table.Chars.Length > 0)
        {
            _chars.CopyFrom(table.Chars);
        }

        _key = context.Alloc(32);
        _hits = context.Alloc(HitCapacity * HitRecordSize);
        _hitCount = context.Alloc(4);
        _verifier = new CandidateEvaluator(new BouncyCastleDeriver(), patterns);
    }

    public string DeviceName => _context.DeviceName;
    public long CandidatesSearched { get; private set; }
    public long ConsistencyErrors { get; private set; }
    public long OverflowDrops { get; private set; }

    /// <summary>
    /// Searches <see cref="CandidatesPerLaunch"/> candidates under a fresh CSPRNG key, in
    /// TDR-safe sub-launches, and returns only the hits the CPU reference reproduces.
    /// </summary>
    public List<Match> RunLaunch()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        _key.CopyFrom(key);
        _hitCount.Clear();

        ulong start = _counter;
        for (int offset = 0; offset < CandidatesPerLaunch; offset += SubLaunchSize)
        {
            int count = Math.Min(SubLaunchSize, CandidatesPerLaunch - offset);
            _context.Launch(_search, (count + BlockSize - 1) / BlockSize, BlockSize,
                _key, start + (ulong)offset, count, _groups, _groupCount, _chars, _hits, _hitCount, HitCapacity);
            _context.Synchronize();
        }

        _counter += CandidatesPerLaunch;
        CandidatesSearched += CandidatesPerLaunch;

        byte[] countBytes = new byte[4];
        _hitCount.CopyTo(countBytes);
        uint hitCount = BitConverter.ToUInt32(countBytes, 0);
        int usable = (int)Math.Min(hitCount, (uint)HitCapacity);
        if (hitCount > HitCapacity)
        {
            OverflowDrops += hitCount - HitCapacity;
        }

        List<Match> matches = new(usable);
        if (usable == 0)
        {
            return matches;
        }

        byte[] raw = new byte[usable * HitRecordSize];
        _hits.CopyTo(raw);
        for (int i = 0; i < usable; i++)
        {
            int at = i * HitRecordSize;
            ReadOnlySpan<byte> entropy = raw.AsSpan(at, 16);
            int len = raw[at + 16];
            string gpuAddress = Encoding.ASCII.GetString(raw, at + 17, len);

            // The gate: print nothing the CPU does not independently reproduce.
            if (_verifier.TryEvaluate(entropy, out Match match) && match.Address == gpuAddress)
            {
                matches.Add(match);
            }
            else
            {
                ConsistencyErrors++;
            }
        }

        return matches;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hitCount.Dispose();
        _hits.Dispose();
        _key.Dispose();
        _chars.Dispose();
        _groups.Dispose();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"`
Expected: PASS (4 tests). Also `-class "*GpuPipelineTests"` must still pass (the `k_prf_entropy` refactor is behavior-preserving). Whole suite green, warning-free.

- [ ] **Step 6: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx src/XrplVanity.Gpu tests/XrplVanity.Tests
git commit -m "feat(gpu): search kernel with hit buffer and a CPU re-verification gate"
```

---

### Task 4: `--gpu` in the CLI with shared output handling

**Files:**
- Modify: `src/XrplVanity.Cli/CliOptions.cs` (add `Gpu`), `src/XrplVanity.Cli/Program.cs` (split into `RunCpu`/`RunGpu`, use `MatchSink`), `src/XrplVanity.Cli/XrplVanity.Cli.csproj` (reference Gpu), `README.md`
- Create: `src/XrplVanity.Cli/MatchSink.cs`
- Test: `tests/XrplVanity.Tests/CliOptionsTests.cs` (add one), `tests/XrplVanity.Tests/MatchSinkTests.cs`, `tests/XrplVanity.Tests/Gpu/GpuCliTests.cs`

**Interfaces:**
- Consumes: `GpuSearchEngine`, `CudaContext.TryCreate`, `Match`, `PatternSet`, `CliOptions`.
- Produces:
  - `bool CliOptions.Gpu` (flag `--gpu`).
  - `sealed class XrplVanity.Cli.MatchSink : IDisposable` — ctor `(int? limit, string? outFile, TextWriter stdout)`; `bool TryReport(Match match)` (prints and returns `true` while under the limit; returns `false` once the limit is reached — including the call that reaches it — without printing surplus matches); `bool CountSatisfied`; `long Reported`.
  - `static List<Match> SearchRunner.SearchGpu(CliOptions options, int launches)` — headless GPU search used by tests (throws `InvalidOperationException` when CUDA is unavailable).

- [ ] **Step 1: Wire the project reference**

```bash
cd xrpl-vanity
dotnet add src/XrplVanity.Cli/XrplVanity.Cli.csproj reference src/XrplVanity.Gpu/XrplVanity.Gpu.csproj
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/XrplVanity.Tests/CliOptionsTests.cs` inside the class:

```csharp
    [Fact]
    public void Gpu_Flag_Is_Off_By_Default_And_Parsed()
    {
        Assert.False(CliOptions.Parse(new[] { "hodl" }).Gpu);
        Assert.True(CliOptions.Parse(new[] { "--gpu", "hodl" }).Gpu);
    }
```

`tests/XrplVanity.Tests/MatchSinkTests.cs`:

```csharp
using System.IO;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class MatchSinkTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Reports_Until_The_Limit_Then_Refuses_Surplus()
    {
        StringWriter stdout = new();
        using MatchSink sink = new(limit: 2, outFile: null, stdout);

        Assert.True(sink.TryReport(M(1)));
        Assert.False(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(2)));   // reaches the limit: printed, but the caller must stop
        Assert.True(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(3)));   // surplus: not printed
        Assert.Equal(2, sink.Reported);

        string text = stdout.ToString();
        Assert.Contains("rAddr1", text);
        Assert.Contains("rAddr2", text);
        Assert.DoesNotContain("rAddr3", text);
    }

    [Fact]
    public void Without_A_Limit_It_Never_Refuses()
    {
        StringWriter stdout = new();
        using MatchSink sink = new(limit: null, outFile: null, stdout);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(sink.TryReport(M(i)));
        }

        Assert.False(sink.CountSatisfied);
        Assert.Equal(10, sink.Reported);
    }

    [Fact]
    public void Appends_Address_And_Seed_To_The_Out_File()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (MatchSink sink = new(limit: null, outFile: path, new StringWriter()))
            {
                sink.TryReport(M(7));
            }

            Assert.Equal("rAddr7\tsEdSeed7", File.ReadAllText(path).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

`tests/XrplVanity.Tests/Gpu/GpuCliTests.cs`:

```csharp
using System.Collections.Generic;
using XrplVanity.Cli;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuCliTests
{
    [Fact]
    public void SearchGpu_Returns_Matches_That_Satisfy_The_Same_Pattern_Set()
    {
        using CudaContext probe = GpuTestHelper.RequireContext(); // skip cleanly without CUDA
        probe.Dispose();

        CliOptions options = CliOptions.Parse(new[] { "--gpu", "--starts", "ab" });
        PatternSet patterns = SearchRunner.BuildPatternSet(options);

        List<Match> matches = SearchRunner.SearchGpu(options, launches: 1);

        Assert.True(matches.Count > 0, "expected ~1,200 hits for a 2-char case-insensitive prefix in 2^20 candidates");
        Assert.All(matches, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*MatchSinkTests"`
Expected: build error — `MatchSink`, `CliOptions.Gpu`, `SearchRunner.SearchGpu` do not exist.

- [ ] **Step 4: Implement**

In `src/XrplVanity.Cli/CliOptions.cs`: add the property `public bool Gpu { get; init; }` after `CaseSensitive`; in `Parse` add a local `bool gpu = false;`, the case

```csharp
                case "--gpu":
                    gpu = true;
                    break;
```

before `case "--threads":`, and `Gpu = gpu,` in the object initializer.

`src/XrplVanity.Cli/MatchSink.cs`:

```csharp
using System;
using System.IO;
using XrplVanity.Core;

namespace XrplVanity.Cli;

/// <summary>
/// The single place matches are written: stdout, the optional --out file, and the --count
/// limit. Thread-safe; shared by the CPU workers and the GPU loop.
/// </summary>
public sealed class MatchSink : IDisposable
{
    private readonly object _gate = new();
    private readonly int? _limit;
    private readonly TextWriter _stdout;
    private readonly TextWriter? _outFile;

    public MatchSink(int? limit, string? outFile, TextWriter stdout)
    {
        _limit = limit;
        _stdout = stdout;
        _outFile = outFile is null ? null : new StreamWriter(outFile, append: true);
    }

    public bool CountSatisfied { get; private set; }
    public long Reported { get; private set; }

    /// <summary>
    /// Writes the match unless the --count limit is already met. Returns false once the limit
    /// is reached (including on the call that reaches it) so the caller stops searching.
    /// </summary>
    public bool TryReport(Match match)
    {
        lock (_gate)
        {
            if (_limit is int already && Reported >= already)
            {
                return false;
            }

            _stdout.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
            _outFile?.WriteLine($"{match.Address}\t{match.Seed}");
            _outFile?.Flush();
            Reported++;

            if (_limit is int limit && Reported >= limit)
            {
                CountSatisfied = true;
                return false;
            }

            return true;
        }
    }

    public void Dispose()
    {
        _outFile?.Dispose();
    }
}
```

Replace `src/XrplVanity.Cli/Program.cs` entirely with:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Cli;

public static class SearchRunner
{
    public static PatternSet BuildPatternSet(CliOptions options)
    {
        PatternSet.Builder builder = new();
        foreach ((MatchPosition position, string pattern) in options.Patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            builder.Add(position, pattern, ignoreCase: !options.CaseSensitive);
        }

        if (options.ListFile is not null)
        {
            foreach (string line in File.ReadLines(options.ListFile))
            {
                string word = line.Trim();
                if (word.Length == 0)
                {
                    continue;
                }

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

    /// <summary>Headless GPU search: runs the given number of launches and returns every verified match.</summary>
    public static List<Match> SearchGpu(CliOptions options, int launches)
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            throw new InvalidOperationException($"CUDA is not available: {reason}");
        }

        using (context)
        {
            PatternSet patterns = BuildPatternSet(options);
            using GpuSearchEngine engine = new(context!, patterns);
            List<Match> all = new();
            for (int i = 0; i < launches; i++)
            {
                all.AddRange(engine.RunLaunch());
            }

            return all;
        }
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

        if (options.Count is int c && c <= 0)
        {
            Console.Error.WriteLine("Option '--count' must be a positive number.");
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            Console.Error.WriteLine(
                $"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
        }

        foreach (NormalizedPattern s in patterns.CaseFoldingSkipped)
        {
            Console.Error.WriteLine(
                $"warning: pattern '{s.Original}' is longer than {PatternSet.MaxIgnoreCaseLength} " +
                "characters; searching it case-sensitively instead of case-insensitively.");
        }

        if (!patterns.HasPatterns)
        {
            Console.Error.WriteLine("error: no reachable patterns to search for.");
            return 2;
        }

        CudaContext? gpuContext = null;
        if (options.Gpu && !CudaContext.TryCreate(out gpuContext, out string reason))
        {
            Console.Error.WriteLine($"error: --gpu requested but CUDA is not available: {reason}");
            return 2;
        }

        Console.Error.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        using MatchSink sink = new(options.Count, options.OutFile, Console.Out);
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (gpuContext is not null)
        {
            using (gpuContext)
            {
                RunGpu(gpuContext, patterns, sink, cts);
            }
        }
        else
        {
            RunCpu(options, patterns, sink, cts);
        }

        // 0 only when a --count target was fully satisfied; otherwise the run ended
        // via Ctrl+C (or a --count-less search the user stopped), reported as 130.
        return sink.CountSatisfied ? 0 : 130;
    }

    private static void RunCpu(CliOptions options, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        void Worker()
        {
            CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
            byte[] pool = new byte[16 * 512];
            int poolPos = pool.Length;

            while (!cts.IsCancellationRequested)
            {
                if (poolPos >= pool.Length)
                {
                    RandomNumberGenerator.Fill(pool);
                    poolPos = 0;
                }

                ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, 16);
                poolPos += 16;

                if (evaluator.TryEvaluate(entropy, out Match match) && !sink.TryReport(match))
                {
                    cts.Cancel();
                    break;
                }
            }
        }

        Thread[] workers = new Thread[Math.Max(1, options.Threads)];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(Worker) { IsBackground = true };
            workers[t].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }
    }

    private static void RunGpu(CudaContext context, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        using GpuSearchEngine engine = new(context, patterns);
        Console.Error.WriteLine($"GPU: {engine.DeviceName}");
        Stopwatch stopwatch = Stopwatch.StartNew();
        long reportedOverflow = 0;
        long reportedErrors = 0;

        while (!cts.IsCancellationRequested)
        {
            foreach (Match match in engine.RunLaunch())
            {
                if (!sink.TryReport(match))
                {
                    cts.Cancel();
                    break;
                }
            }

            if (engine.OverflowDrops > reportedOverflow)
            {
                Console.Error.WriteLine(
                    $"warning: {engine.OverflowDrops - reportedOverflow:N0} hits dropped this launch " +
                    $"(hit buffer holds {GpuSearchEngine.HitCapacity}); use a more selective pattern.");
                reportedOverflow = engine.OverflowDrops;
            }

            if (engine.ConsistencyErrors > reportedErrors)
            {
                Console.Error.WriteLine(
                    $"GPU consistency error: {engine.ConsistencyErrors - reportedErrors} device hit(s) the CPU " +
                    "reference could not reproduce were discarded.");
                reportedErrors = engine.ConsistencyErrors;
            }
        }

        double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-9);
        Console.Error.WriteLine(
            $"GPU: searched {engine.CandidatesSearched:N0} candidates in {seconds:F1} s " +
            $"({engine.CandidatesSearched / seconds:N0}/s); consistency errors: {engine.ConsistencyErrors}.");
    }
}
```

README: in the "Flags" table add `| \`--gpu\` | off | search on the NVIDIA GPU (requires the driver; every GPU hit is re-verified by the CPU before it is printed) |`; in the "GPU backend (CUDA)" section replace the sentence saying `--gpu` is not implemented with: "`--gpu` runs the search on the device. The host builds the same pattern set the CPU path uses and uploads it; the kernel derives and matches candidates on the GPU; every reported hit is re-derived by the CPU reference and printed only if it agrees, so a GPU arithmetic error can cost throughput but never a wrong key. Without an NVIDIA driver `--gpu` exits with code 2 rather than silently using the CPU."

- [ ] **Step 5: Run tests to verify they pass**

Run: `-class "*MatchSinkTests"`, `-class "*CliOptionsTests"`, `-class "*GpuCliTests"`, `-class "*CliRuntimeTests"` (existing CPU headless tests must still pass), then the whole suite. Expected: all green, warning-free.

- [ ] **Step 6: Smoke-test the CLI end to end**

Run: `dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts abc --count 2`
Expected: the secret warning, `GPU: NVIDIA GeForce RTX 5070 Ti`, two `Address: … Seed: …` lines, a final `GPU: searched … candidates in … s (…/s); consistency errors: 0.` line, exit code 0. Then `dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts a --count 1` must print an overflow warning (still exit 0). Record the measured `/s` rate in the commit body.

- [ ] **Step 7: Commit**

```bash
git add src/XrplVanity.Cli tests/XrplVanity.Tests README.md
git commit -m "feat(cli): --gpu search with CPU-verified output shared through MatchSink"
```

---

### Task 5: Windowed fixed-base scalar multiplication, proven equal to the naive path (M6, part 1)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the M6 section), regenerate PTX
- Test: `tests/XrplVanity.Tests/Gpu/GpuWindowedTests.cs`

**Interfaces:**
- Consumes: `scalarmult_base` (naive, verified), `pt_dbl`, `fe_*`, `D2_LIMBS`, `BX_LIMBS`, `BY_LIMBS`.
- Produces (device): `struct Precomp { Fe ypx, ymx, xy2d; }`; `k_build_base_table(u8* table)` — 256 entries × 96 bytes, entry `k*8 + j` = precomp of `(j+1) * 2^(8k) * B`; `pt_madd`; `scalarmult_base_windowed(Pt& r, const u8* scalar32, const u8* table)` (scalar must be clamped: bit 255 clear); test entry `k_scalarmult_base_windowed(const u8* scalars, u8* pubs, int n, const u8* table)`.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuWindowedTests.cs`:

```csharp
using System;
using System.Numerics;
using System.Security.Cryptography;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuWindowedTests
{
    private const int TableBytes = 256 * 96;

    private static byte[] Clamp(byte[] seed32)
    {
        byte[] h = SHA512.HashData(seed32);
        byte[] a = new byte[32];
        Array.Copy(h, a, 32);
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;
        return a;
    }

    private static DeviceBuffer BuildTable(CudaContext context, CudaModule module)
    {
        DeviceBuffer table = context.Alloc(TableBytes);
        context.Launch(module.GetFunction("k_build_base_table"), 2, 128, table);
        context.Synchronize();
        return table;
    }

    private static byte[] Run(string kernel, byte[][] scalars)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        using DeviceBuffer ownTable = BuildTable(context, module);

        int n = scalars.Length;
        byte[] input = new byte[n * 32];
        for (int i = 0; i < n; i++)
        {
            scalars[i].CopyTo(input, i * 32);
        }

        using DeviceBuffer din = context.Alloc(n * 32);
        using DeviceBuffer dout = context.Alloc(n * 32);
        din.CopyFrom(input);
        if (kernel == "k_scalarmult_base_windowed")
        {
            context.Launch(module.GetFunction(kernel), (n + 127) / 128, 128, din, dout, n, ownTable);
        }
        else
        {
            context.Launch(module.GetFunction(kernel), (n + 127) / 128, 128, din, dout, n);
        }

        context.Synchronize();
        byte[] output = new byte[n * 32];
        dout.CopyTo(output);
        return output;
    }

    [Fact]
    public void Table_Entry_Zero_Is_The_Base_Point()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        using DeviceBuffer table = BuildTable(context, module);
        byte[] raw = new byte[96];
        table.CopyTo(raw);

        BigInteger p = GpuTestHelper.P;
        BigInteger bx = BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202");
        BigInteger by = BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960");
        BigInteger d = BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");

        BigInteger ypx = new(raw.AsSpan(0, 32), isUnsigned: true, isBigEndian: false);
        BigInteger ymx = new(raw.AsSpan(32, 32), isUnsigned: true, isBigEndian: false);
        BigInteger xy2d = new(raw.AsSpan(64, 32), isUnsigned: true, isBigEndian: false);

        Assert.Equal((by + bx) % p, ypx);
        Assert.Equal(((by - bx) % p + p) % p, ymx);
        Assert.Equal(2 * d % p * bx % p * by % p, xy2d);
    }

    [Theory]
    [InlineData("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60", "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")]
    [InlineData("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb", "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c")]
    [InlineData("c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7", "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025")]
    public void Windowed_Reproduces_Rfc8032_Public_Keys(string seedHex, string publicHex)
    {
        byte[] pub = Run("k_scalarmult_base_windowed", new[] { Clamp(Convert.FromHexString(seedHex)) });

        Assert.Equal(publicHex, Convert.ToHexString(pub).ToLowerInvariant());
    }

    [Fact]
    public void Windowed_Equals_Naive_For_Random_Clamped_Scalars()
    {
        const int n = 1000;
        Random rng = new(20260903);
        byte[][] scalars = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            byte[] seed = new byte[32];
            rng.NextBytes(seed);
            scalars[i] = Clamp(seed);
        }

        byte[] naive = Run("k_scalarmult_base", scalars);
        byte[] windowed = Run("k_scalarmult_base_windowed", scalars);

        Assert.True(naive.AsSpan().SequenceEqual(windowed), "windowed and naive scalar multiplication disagree");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuWindowedTests"`
Expected: FAIL — `cuModuleGetFunction(k_build_base_table)` named symbol not found.

- [ ] **Step 3: Append the M6 section and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
// ---------------------------------------------------------------------------
// M6: windowed fixed-base scalar multiplication (libsodium ge25519_scalarmult_base
// layout). A 256-entry table of precomputed multiples of B is built ON THE DEVICE by
// the verified naive scalarmult_base, so no new host arithmetic is trusted.
//   table[k*8 + j] = precomp((j+1) * 2^(8k) * B), k in 0..31, j in 0..7, 96 bytes each
//   precomp(P) = (y+x, y-x, 2d*x*y) of the affine point.
// Requires a clamped scalar (bit 255 clear) so the recoded top digit fits the table.
// ---------------------------------------------------------------------------
struct Precomp { Fe ypx, ymx, xy2d; };

__device__ void precomp_from_pt(Precomp& o, const Pt& p)
{
    Fe zi, x, y, t, d2;
    fe_inv(zi, p.Z);
    fe_mul(x, p.X, zi);
    fe_mul(y, p.Y, zi);
    fe_add(o.ypx, y, x);
    fe_sub(o.ymx, y, x);
    fe_load_const(d2, D2_LIMBS);
    fe_mul(t, x, y);
    fe_mul(o.xy2d, t, d2);
}

extern "C" __global__ void k_build_base_table(u8* table)
{
    int t = blockIdx.x * blockDim.x + threadIdx.x;
    if (t >= 256) return;
    int k = t / 8;
    int j = t % 8;
    u8 s[32];
    for (int i = 0; i < 32; i++) s[i] = 0;
    s[k] = (u8)(j + 1);                       // (j+1) * 256^k = (j+1) * 2^(8k)
    Pt P;
    scalarmult_base(P, s);
    Precomp pc;
    precomp_from_pt(pc, P);
    u8* o = table + t * 96;
    fe_to_bytes(o, pc.ypx);
    fe_to_bytes(o + 32, pc.ymx);
    fe_to_bytes(o + 64, pc.xy2d);
}

__device__ __forceinline__ void precomp_identity(Precomp& o) { fe_one(o.ypx); fe_one(o.ymx); fe_zero(o.xy2d); }

__device__ __forceinline__ void precomp_load(Precomp& o, const u8* table, int index)
{
    const u8* e = table + index * 96;
    fe_from_bytes(o.ypx, e);
    fe_from_bytes(o.ymx, e + 32);
    fe_from_bytes(o.xy2d, e + 64);
}

// -P: (y+x, y-x, 2dxy) -> (y-x, y+x, -2dxy)
__device__ __forceinline__ void precomp_neg(Precomp& o)
{
    Fe t;
    fe_copy(t, o.ypx);
    fe_copy(o.ypx, o.ymx);
    fe_copy(o.ymx, t);
    Fe z;
    fe_zero(z);
    fe_sub(o.xy2d, z, o.xy2d);
}

// r = p + q with q in precomputed affine form (add-2008-hwcd-3, Z2 = 1). r may alias p.
__device__ void pt_madd(Pt& r, const Pt& p, const Precomp& q)
{
    Fe A, B, C, D, E, F, G, H, t1;
    fe_sub(t1, p.Y, p.X); fe_mul(A, t1, q.ymx);
    fe_add(t1, p.Y, p.X); fe_mul(B, t1, q.ypx);
    fe_mul(C, p.T, q.xy2d);
    fe_add(D, p.Z, p.Z);
    fe_sub(E, B, A); fe_sub(F, D, C); fe_add(G, D, C); fe_add(H, B, A);
    fe_mul(r.X, E, F); fe_mul(r.Y, G, H); fe_mul(r.Z, F, G); fe_mul(r.T, E, H);
}

__device__ __forceinline__ void select_precomp(Precomp& o, const u8* table, int k, int e)
{
    if (e == 0) { precomp_identity(o); return; }
    int a = e < 0 ? -e : e;
    precomp_load(o, table, k * 8 + (a - 1));
    if (e < 0) precomp_neg(o);
}

// r = scalar * B via 64 signed 4-bit digits: odd digits first, x16, then even digits.
__device__ void scalarmult_base_windowed(Pt& r, const u8* a, const u8* table)
{
    signed char e[64];
    for (int i = 0; i < 32; i++) {
        e[2 * i]     = (signed char)(a[i] & 15);
        e[2 * i + 1] = (signed char)((a[i] >> 4) & 15);
    }
    signed char carry = 0;
    for (int i = 0; i < 63; i++) {
        e[i] = (signed char)(e[i] + carry);
        carry = (signed char)((e[i] + 8) >> 4);
        e[i] = (signed char)(e[i] - carry * 16);
    }
    e[63] = (signed char)(e[63] + carry);

    Pt h;
    pt_identity(h);
    Precomp t;
    for (int i = 1; i < 64; i += 2) {
        select_precomp(t, table, i / 2, e[i]);
        pt_madd(h, h, t);
    }
    pt_dbl(h, h); pt_dbl(h, h); pt_dbl(h, h); pt_dbl(h, h);
    for (int i = 0; i < 64; i += 2) {
        select_precomp(t, table, i / 2, e[i]);
        pt_madd(h, h, t);
    }
    r = h;
}

// Test entry: n clamped scalars -> n compressed public keys via the windowed path.
extern "C" __global__ void k_scalarmult_base_windowed(const u8* scalars, u8* pubs, int n, const u8* table)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    Pt P;
    scalarmult_base_windowed(P, scalars + i * 32, table);
    pt_compress(pubs + i * 32, P);
}
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuWindowedTests"`
Expected: PASS (5 tests: table anchor against the host constants, three RFC vectors through the windowed path, 1,000 random clamped scalars windowed == naive). Whole suite green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): windowed fixed-base scalar multiplication with a device-built table, proven equal to the naive path"
```

---

### Task 6: Switch the pipeline to the windowed path, re-run the 1,000,000 cross-check, benchmark (M6, part 2)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (`derive_address`, `k_derive_address`, `k_search` gain a `table` parameter), regenerate PTX
- Modify: `src/XrplVanity.Gpu/GpuSearchEngine.cs` (build and pass the table), `tests/XrplVanity.Tests/Gpu/GpuPipelineTests.cs` (build and pass the table), `README.md` (performance note)
- Test: `tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs` (add the benchmark)

**Interfaces:**
- Consumes: `scalarmult_base_windowed`, `k_build_base_table`, everything from Task 3.
- Produces: `derive_address(const u8* entropy16, u8* out36, const u8* table)`; `k_derive_address(const u8* entropy, u8* out, int n, const u8* table)`; `k_search(…, int hitCapacity, const u8* table)` (table appended as the LAST parameter); `GpuSearchEngine` builds the table once in its constructor.

- [ ] **Step 1: Write the failing test**

Append to `tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs` inside the class (and add `using System.Diagnostics;` plus a constructor taking `Xunit.ITestOutputHelper output` stored in a field `_output`):

```csharp
    [Fact]
    public void Throughput_Is_At_Least_Half_A_Million_Candidates_Per_Second()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        // 8 characters case-sensitive: effectively never matches, so this measures pure search throughput.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using GpuSearchEngine engine = new(context, patterns);
        engine.RunLaunch(); // warm-up (module JIT, first launch)

        Stopwatch stopwatch = Stopwatch.StartNew();
        const int launches = 4;
        for (int i = 0; i < launches; i++)
        {
            engine.RunLaunch();
        }

        double seconds = stopwatch.Elapsed.TotalSeconds;
        double rate = (double)launches * GpuSearchEngine.CandidatesPerLaunch / seconds;
        _output.WriteLine($"GPU search throughput: {rate:N0} candidates/s on {engine.DeviceName}");

        Assert.True(rate >= 500_000, $"throughput {rate:N0}/s is below the 500,000/s floor");
    }
```

Then in `GpuPipelineTests.RunDerive`, the kernel now needs the table: this step's expectation is that the current suite FAILS until the kernel and the harness are both updated (the `k_derive_address` parameter list changes).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"`
Expected: build error — `_output` / constructor missing until added; after adding them, the benchmark itself passes only once Step 3 is done (it measures the naive path otherwise — note the number, it should roughly quintuple after the switch).

- [ ] **Step 3: Kernel — thread the table through the pipeline; regenerate PTX**

In `gpu/kernels/xrpl_vanity.cu` make exactly these edits:

1. `derive_address` signature becomes `__device__ void derive_address(const u8* entropy16, u8* out36, const u8* table)` and its scalar multiplication line `scalarmult_base(P, a);` becomes `scalarmult_base_windowed(P, a, table);`. Because `scalarmult_base_windowed` is defined in the M6 section AFTER `derive_address`, add a forward declaration immediately above `derive_address`:

```cpp
__device__ void scalarmult_base_windowed(Pt& r, const u8* a, const u8* table);
```

2. `k_derive_address` becomes:

```cpp
extern "C" __global__ void k_derive_address(const u8* entropy, u8* out, int n, const u8* table)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    derive_address(entropy + i * 16, out + i * 36, table);
}
```

3. `k_search` gains a trailing parameter `const u8* table` and calls `derive_address(e, rec, table);`.

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Host — build the table in the engine and in the pipeline test**

In `src/XrplVanity.Gpu/GpuSearchEngine.cs`:
- add a field `private readonly DeviceBuffer _table;` and `private const int TableBytes = 256 * 96;`
- in the constructor, after `_search = module.GetFunction("k_search");`, add:

```csharp
        _table = context.Alloc(TableBytes);
        context.Launch(module.GetFunction("k_build_base_table"), 2, 128, _table);
        context.Synchronize();
```

- append `_table` as the LAST argument of the `_context.Launch(_search, …)` call (after `HitCapacity`);
- dispose `_table` in `Dispose()`.

In `tests/XrplVanity.Tests/Gpu/GpuPipelineTests.cs`, change `RunDerive` so that after loading the module it builds the table and passes it:

```csharp
        using DeviceBuffer table = context.Alloc(256 * 96);
        context.Launch(module.GetFunction("k_build_base_table"), 2, 128, table);
        context.Synchronize();
```

and every `k_derive_address` launch passes `table` as the last argument (after the count).

README: in the GPU section add one sentence: "The kernel uses a windowed fixed-base scalar multiplication over a 256-entry table that is built on the device at start-up by the naive (independently verified) multiplication; both paths are checked against RFC 8032 and each other in the test suite."

- [ ] **Step 5: Run the anchors and the benchmark**

Run, in order:
- `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuWindowedTests"` — anchor 2 via the windowed path: PASS.
- `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPipelineTests"` — anchor 4: the known vector and the 1,000,000-input cross-check now run through the windowed path: PASS with 0 mismatches.
- `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"` — the search tests and the benchmark: PASS; read the printed throughput.
- The whole suite: green, warning-free.

Then the CLI: `dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts abc --count 3` — record the final `…/s` line. Put both throughput numbers (test and CLI) in the commit body.

- [ ] **Step 6: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx src/XrplVanity.Gpu tests/XrplVanity.Tests README.md
git commit -m "perf(gpu): derive addresses with the windowed scalar multiplication, re-verified over 1,000,000 inputs"
```

---

## Notes for the executor

- The device matcher decides hit / no-hit only; the CPU gate produces the final `Match`. If `GpuSearchEngine.ConsistencyErrors` is ever non-zero in a test, that is a real device bug — do not weaken the gate.
- `k_prf_entropy`'s behaviour must not change in Task 3 (the existing `Prf_Entropy_Matches_Sha512_Of_Key_And_Counter` test pins it).
- Task 6 changes kernel parameter lists; every launch site (`GpuSearchEngine`, `GpuPipelineTests.RunDerive`) must pass the table or the driver reports `CUDA_ERROR_INVALID_VALUE` / garbage output.
- Regenerate and commit the PTX with every kernel change. Do not push, publish, or open a pull request.
