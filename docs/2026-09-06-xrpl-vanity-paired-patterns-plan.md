# Paired Patterns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a pattern that requires a prefix and a suffix at the same time, searched on the CPU and on every GPU backend, expressed in the CLI and in the desktop application, and accounted for exactly by the estimate.

**Architecture:** `PatternSet` gains a third rule kind — a pair of buckets, one per half — checked before the existing or-conditions; `TargetTable` marks a pair's halves with position codes the kernel's or-loop skips and adds a `Pairs` index buffer; the kernel gains a second loop that requires both halves; `SearchEstimator` multiplies the two halves' probabilities; the CLI gains `--starts-ends PREFIX:SUFFIX`; the desktop pattern editor gains a fourth entry mode.

**Tech Stack:** .NET 10, C# latest, xUnit v3 4.0.0 run as an executable, bUnit 2.9.0, CUDA PTX built by `gpu/build-ptx.py` (NVRTC), OpenCL C compiled at run time from the same source.

**Spec:** `docs/2026-09-06-xrpl-vanity-paired-patterns-design.md`

## Global Constraints

- Work in the worktree `E:\GIT\XRPL\xrpl-vanity\.worktrees\paired-patterns` on branch `feature/paired-patterns`. Never `git stash`.
- Tests are xUnit v3 run as executables: `dotnet run --project tests/XrplVanity.Tests -c Release [-- -class "*Name"]` and `dotnet run --project tests/XrplVanity.App.Tests -c Release`. **Never `dotnet test`.** The Windows suite is 254 tests and the App suite 107, both 0 failed and 0 skipped before this work starts.
- GPU-dependent tests skip through `GpuTestHelper.SkipOrFail` when hardware is absent; this machine has CUDA and OpenCL on one RTX 5070 Ti, so they run for real.
- The CLI contract does not change: stdout `Address: {address}\tSeed: {seed}`, `--out` lines `{address}\t{seed}`, exit codes 0 / 2 / 130, and every existing warning and error text.
- `--starts`, `--ends`, `--contains` and several patterns together keep meaning **or**.
- The reported form of a paired rule is `{starts}..{ends}` with two ASCII dots.
- Position codes in `TargetTable`: `PositionStarts` 0, `PositionEnds` 1, `PositionContains` 2, `PositionPairStarts` 3, `PositionPairEnds` 4.
- Style: explicit types (no `var`), `_camelCase` private fields, English comments only where they explain a decision.
- Commits: conventional, English, **no `Co-Authored-By` or any other attribution trailer**.

---

## File structure

| Path | Change |
| --- | --- |
| `src/XrplVanity.Core/PatternSet.cs` | `Pair`, `Pairs`, `Builder.AddPair`, `TryMatch` checks pairs first, `HasPatterns` |
| `src/XrplVanity.Core/Match.cs` (or wherever `MatchPosition` lives) | `MatchPosition.Pair` |
| `src/XrplVanity.Search/SearchEstimator.cs` | pairs in `ExpectedCandidatesPerMatch` |
| `src/XrplVanity.Gpu/TargetTable.cs` | pair position codes, `Pairs` buffer, shared halves |
| `gpu/kernels/xrpl_vanity_body.h` | `matches_any` skips pair halves and evaluates pairs; two kernels take the extra parameters |
| `gpu/kernels/xrpl_vanity.ptx` | rebuilt, new `body-sha256` stamp |
| `src/XrplVanity.Gpu/GpuSearchEngine.cs` | allocates and passes the pairs buffer |
| `src/XrplVanity.Cli/CliOptions.cs`, `Program.cs` | `--starts-ends` |
| `src/XrplVanity.App.Core/…` | fourth entry mode, chip, warnings |
| `linux/in-container.sh` | one smoke |
| `README.md`, `docs/benchmarks.md`, `docs/desktop-app.md` | documentation |

---

### Task 1: `PatternSet` learns paired rules

**Files:**
- Modify: `src/XrplVanity.Core/PatternSet.cs`
- Modify: the file declaring `MatchPosition` (find it: `grep -rn "enum MatchPosition" src/XrplVanity.Core`)
- Test: `tests/XrplVanity.Tests/PatternSetTests.cs`

**Interfaces:**
- Produces: `MatchPosition.Pair`; `PatternSet.Pair` with `IReadOnlyCollection<string> Starts`, `IReadOnlyCollection<string> Ends`, `int StartsLength`, `int EndsLength`, `string Display`; `PatternSet.Pairs` (`IReadOnlyList<Pair>`); `PatternSet.Builder.AddPair(string starts, string ends, bool ignoreCase)`; `HasPatterns` true when a pair exists; `TryMatch` reporting `MatchPosition.Pair` and `Display`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/XrplVanity.Tests/PatternSetTests.cs` (keep the file's existing style and helpers):

```csharp
    private static PatternSet Pair(string starts, string ends, bool ignoreCase = false) =>
        new PatternSet.Builder().AddPair(starts, ends, ignoreCase).Build();

    [Fact]
    public void A_Pair_Matches_Only_When_Both_Halves_Hold()
    {
        PatternSet set = Pair("ab", "yz");

        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);

        Assert.False(set.TryMatch("rabQQQQQQQQQ", out _, out _));   // prefix only
        Assert.False(set.TryMatch("rQQQQQQQQQyz", out _, out _));   // suffix only
        Assert.False(set.TryMatch("rQQQQQQQQQQQ", out _, out _));   // neither
    }

    [Fact]
    public void A_Pair_Whose_Halves_Do_Not_Fit_The_Body_Does_Not_Match()
    {
        PatternSet set = Pair("abcd", "wxyz");
        Assert.False(set.TryMatch("rabcdwxyz", out _, out _));      // body is 8, halves need 8 — fits exactly
        Assert.True(set.TryMatch("rabcdQwxyz", out _, out _));      // body is 9
    }

    [Fact]
    public void The_Two_Halves_Are_Normalized_Independently()
    {
        // l -> L and I -> i, per PatternNormalizer.
        PatternSet set = Pair("Il", "0O");
        Assert.True(set.TryMatch("riLQQQQQQQoo", out _, out string pattern));
        Assert.Equal("Il..0O", pattern);   // Display keeps what the user typed
    }

    [Fact]
    public void An_Unreachable_Half_Makes_The_Whole_Rule_Unreachable()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "@", ignoreCase: false).Build();
        Assert.False(set.HasPatterns);
        NormalizedPattern reported = Assert.Single(set.Unreachable);
        Assert.Equal("ab..@", reported.Original);
    }

    [Fact]
    public void An_Empty_Half_Makes_The_Rule_Unreachable()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "   ", ignoreCase: false).Build();
        Assert.False(set.HasPatterns);
        Assert.Single(set.Unreachable);
    }

    [Fact]
    public void Case_Folding_Expands_Each_Half_On_Its_Own()
    {
        PatternSet set = Pair("ab", "yz", ignoreCase: true);
        PatternSet.Pair pair = Assert.Single(set.Pairs);
        Assert.Equal(4, pair.Starts.Count);   // ab aB Ab AB
        Assert.Equal(4, pair.Ends.Count);     // yz yZ Yz YZ
        Assert.True(set.TryMatch("rABQQQQQQQYz", out _, out _));
    }

    [Fact]
    public void A_Half_Longer_Than_The_Case_Folding_Cap_Is_Reported_And_Searched_Case_Sensitively()
    {
        string longHalf = new string('a', PatternSet.MaxIgnoreCaseLength + 1);
        PatternSet set = new PatternSet.Builder().AddPair(longHalf, "yz", ignoreCase: true).Build();
        PatternSet.Pair pair = Assert.Single(set.Pairs);
        Assert.Single(pair.Starts);           // not expanded
        Assert.Equal(4, pair.Ends.Count);     // still expanded
        Assert.Single(set.CaseFoldingSkipped);
    }

    [Fact]
    public void A_Pair_Is_Reported_Before_A_Plain_Pattern_That_Also_Holds()
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "yz", ignoreCase: false)
            .Add(MatchPosition.Starts, "ab", ignoreCase: false)
            .Build();

        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternSetTests"`
Expected: build errors — `AddPair`, `Pairs`, `MatchPosition.Pair` do not exist.

- [ ] **Step 3: Implement**

In the `MatchPosition` enum add `Pair` **after** the existing members, so no existing serialized value shifts.

In `PatternSet`:

```csharp
    /// <summary>
    /// One rule that requires both halves at once: the body must start with one of
    /// <see cref="Starts"/> and end with one of <see cref="Ends"/>. Each half holds the
    /// expanded, normalized variants of that half only, so case folding stays additive
    /// (4 + 4 variants for "ab" + "yz", not 16 combinations).
    /// </summary>
    public sealed class Pair
    {
        internal Pair(HashSet<string> starts, HashSet<string> ends, string display)
        {
            StartsSet = starts;
            EndsSet = ends;
            StartsLookup = starts.GetAlternateLookup<ReadOnlySpan<char>>();
            EndsLookup = ends.GetAlternateLookup<ReadOnlySpan<char>>();
            StartsLength = starts.First().Length;
            EndsLength = ends.First().Length;
            Display = display;
        }

        internal HashSet<string> StartsSet { get; }
        internal HashSet<string> EndsSet { get; }
        internal HashSet<string>.AlternateLookup<ReadOnlySpan<char>> StartsLookup { get; }
        internal HashSet<string>.AlternateLookup<ReadOnlySpan<char>> EndsLookup { get; }

        public IReadOnlyCollection<string> Starts => StartsSet;
        public IReadOnlyCollection<string> Ends => EndsSet;
        public int StartsLength { get; }
        public int EndsLength { get; }
        public string Display { get; }
    }

    public IReadOnlyList<Pair> Pairs => _pairs;
```

`_pairs` is a `List<Pair>` passed through the private constructor beside the existing fields.
`HasPatterns` gains `|| _pairs.Count > 0`. In `TryMatch`, before the prefix check:

```csharp
        foreach (Pair pair in _pairs)
        {
            if (pair.StartsLength + pair.EndsLength > body.Length)
            {
                continue;
            }

            if (pair.StartsLookup.Contains(body[..pair.StartsLength])
                && pair.EndsLookup.Contains(body[^pair.EndsLength..]))
            {
                position = MatchPosition.Pair;
                pattern = pair.Display;
                return true;
            }
        }
```

In `Builder`:

```csharp
        /// <summary>
        /// Adds a rule that requires both halves. Either half unreachable or empty after
        /// normalization makes the whole rule unreachable; case folding is applied and capped
        /// per half exactly as it is for a single pattern.
        /// </summary>
        public Builder AddPair(string starts, string ends, bool ignoreCase)
        {
            string display = $"{starts}..{ends}";
            NormalizedPattern left = PatternNormalizer.Normalize(starts.Trim());
            NormalizedPattern right = PatternNormalizer.Normalize(ends.Trim());

            if (!left.Reachable || !right.Reachable || left.Value.Length == 0 || right.Value.Length == 0)
            {
                _unreachable.Add(new NormalizedPattern($"{left.Value}..{right.Value}", false, display));
                return this;
            }

            HashSet<string> leftSet = ExpandHalf(left, ignoreCase);
            HashSet<string> rightSet = ExpandHalf(right, ignoreCase);
            _pairs.Add(new Pair(leftSet, rightSet, display));
            return this;
        }

        /// <summary>Case variants of one already-normalized half, capped like a single pattern.</summary>
        private HashSet<string> ExpandHalf(NormalizedPattern normalized, bool ignoreCase)
        {
            bool expand = ignoreCase && normalized.Value.Length <= MaxIgnoreCaseLength;
            if (ignoreCase && !expand)
            {
                _caseFoldingSkipped.Add(normalized);
            }

            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (string variant in expand ? CaseVariants(normalized.Value) : new[] { normalized.Value })
            {
                set.Add(variant);
            }

            return set;
        }
```

Reuse the existing private `CaseVariants`; if it is not already static and reachable from here, make it so without changing its behaviour. `Build()` passes `_pairs` to the constructor.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*PatternSetTests"`
Expected: every test passes, including the pre-existing ones.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core tests/XrplVanity.Tests/PatternSetTests.cs
git commit -m "feat(core): a pattern set can require a prefix and a suffix together"
```

---

### Task 2: the estimate accounts for pairs

**Files:**
- Modify: `src/XrplVanity.Search/SearchEstimator.cs`
- Test: `tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs`

**Interfaces:**
- Consumes: `PatternSet.Pairs`, `PatternSet.Pair.Starts/Ends/StartsLength/EndsLength`, the existing `LeadingCharacterProbability` and `AlphabetSize`.
- Produces: `ExpectedCandidatesPerMatch` including pairs; no signature change.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void A_Pair_Multiplies_The_Two_Halves()
    {
        PatternSet set = new PatternSet.Builder().AddPair("abc", "xyz", ignoreCase: false).Build();
        double expected = 1.0 / (SearchEstimator.LeadingCharacterProbability('a')
                                 * Math.Pow(58, -2)   // the rest of "abc"
                                 * Math.Pow(58, -3)); // "xyz"
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(set)!.Value, precision: 3);
        Assert.InRange(SearchEstimator.ExpectedCandidatesPerMatch(set)!.Value, 1.5e10, 1.6e10);
    }

    [Fact]
    public void A_Case_Insensitive_Pair_Counts_Every_Variant_Of_Both_Halves()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "yz", ignoreCase: true).Build();
        double starts = SearchEstimator.LeadingCharacterProbability('a') * Math.Pow(58, -1)
                      + SearchEstimator.LeadingCharacterProbability('A') * Math.Pow(58, -1);
        double ends = 4 * Math.Pow(58, -2);
        Assert.Equal(1.0 / (starts * ends), SearchEstimator.ExpectedCandidatesPerMatch(set)!.Value, precision: 3);
    }

    [Fact]
    public void A_Pair_And_A_Plain_Pattern_Add_Their_Probabilities()
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("abc", "xyz", ignoreCase: false)
            .Add(MatchPosition.Starts, "a", ignoreCase: false)
            .Build();
        double pair = SearchEstimator.LeadingCharacterProbability('a') * Math.Pow(58, -2) * Math.Pow(58, -3);
        double plain = SearchEstimator.LeadingCharacterProbability('a');
        Assert.Equal(1.0 / (pair + plain), SearchEstimator.ExpectedCandidatesPerMatch(set)!.Value, precision: 6);
    }

    [Fact]
    public void A_Set_With_Only_An_Unreachable_Pair_Has_No_Estimate()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "@", ignoreCase: false).Build();
        Assert.Null(SearchEstimator.ExpectedCandidatesPerMatch(set));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchEstimatorTests"`
Expected: the pair values come out as the old `null`/plain-only figures.

- [ ] **Step 3: Implement**

In `ExpectedCandidatesPerMatch`, after the loop over `EnumerateTargets()`:

```csharp
        foreach (PatternSet.Pair pair in patterns.Pairs)
        {
            double startsProbability = 0;
            foreach (string variant in pair.Starts)
            {
                startsProbability += LeadingCharacterProbability(variant[0]) * Math.Pow(AlphabetSize, -(pair.StartsLength - 1));
            }

            // The two halves sit at fixed, non-overlapping ends of the body, so they are
            // independent and their probabilities multiply.
            probability += startsProbability * pair.Ends.Count * Math.Pow(AlphabetSize, -pair.EndsLength);
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchEstimatorTests"`

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Search/SearchEstimator.cs tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs
git commit -m "feat(search): the estimate multiplies the halves of a paired pattern"
```

---

### Task 3: the target table carries pairs

**Files:**
- Modify: `src/XrplVanity.Gpu/TargetTable.cs`
- Test: `tests/XrplVanity.Tests/Gpu/GpuMatcherTests.cs` (host-side table tests; find the existing `TargetTable` tests first with `grep -rn "TargetTable" tests/`)

**Interfaces:**
- Produces: `TargetTable.PositionPairStarts` = 3, `PositionPairEnds` = 4, `uint[] Pairs`, `int PairCount`; the halves of a pair emitted as ordinary groups carrying the pair-only position codes, deduplicated so two rules with the same half and length share one group.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void A_Pair_Emits_Two_Pair_Only_Groups_And_One_Index_Entry()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "yz", ignoreCase: false).Build();
        TargetTable table = TargetTable.Build(set);

        Assert.Equal(2, table.GroupCount);
        Assert.Equal(1, table.PairCount);
        uint startsGroup = table.Pairs[0];
        uint endsGroup = table.Pairs[1];
        Assert.Equal(TargetTable.PositionPairStarts, table.Groups[startsGroup * 4]);
        Assert.Equal(TargetTable.PositionPairEnds, table.Groups[endsGroup * 4]);
        Assert.Equal(2u, table.Groups[startsGroup * 4 + 1]);
        Assert.Equal(2u, table.Groups[endsGroup * 4 + 1]);
    }

    [Fact]
    public void Two_Pairs_Sharing_A_Half_Share_Its_Group()
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "yz", ignoreCase: false)
            .AddPair("ab", "wx", ignoreCase: false)
            .Build();
        TargetTable table = TargetTable.Build(set);

        Assert.Equal(2, table.PairCount);
        Assert.Equal(table.Pairs[0], table.Pairs[2]);       // the same "ab" group
        Assert.NotEqual(table.Pairs[1], table.Pairs[3]);
    }

    [Fact]
    public void A_Set_Without_Pairs_Has_An_Empty_Pairs_Buffer_And_Unchanged_Groups()
    {
        PatternSet set = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: false).Build();
        TargetTable table = TargetTable.Build(set);

        Assert.Equal(0, table.PairCount);
        Assert.Empty(table.Pairs);
        Assert.Equal(TargetTable.PositionStarts, table.Groups[0]);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"`

- [ ] **Step 3: Implement**

Add the two constants and the buffer, and extend `Build`: after the existing loop over
`EnumerateTargets()`, walk `patterns.Pairs`, adding each half to the same `grouped` dictionary
under the key `(PositionPairStarts, length)` / `(PositionPairEnds, length)`. Because the
dictionary is keyed by position and length and holds a set of strings, halves of the same
length merge into one group automatically — which is what "share a group" means, and it is
correct only because a pair's half is *identified* by its group index plus the variants it
contains. To keep that identification exact, build the pairs index **after** the groups list is
final: record, per rule, the key of each half, then translate keys to indices in a second pass
over the ordered `grouped` entries. Two rules whose halves have the same length but different
variants therefore end up in one group holding the union — which would let a wrong combination
match. Prevent that by keying pair halves on `(position, length, ruleIndex)` instead, so each
rule gets its own groups, and deduplicate only when two rules have byte-identical halves
(compare the sorted variant lists). Document that choice in a comment: correctness first, one
group per distinct half.

`Pairs` is `uint[]` with two entries per rule; `PairCount => Pairs.Length / 2`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"`

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Gpu/TargetTable.cs tests/XrplVanity.Tests/Gpu
git commit -m "feat(gpu): the target table emits pair-only groups and a pairs index"
```

---

### Task 4: the kernel evaluates pairs

**Files:**
- Modify: `gpu/kernels/xrpl_vanity_body.h`
- Modify: `gpu/kernels/xrpl_vanity.ptx` (rebuilt, not hand-edited)
- Modify: `src/XrplVanity.Gpu/GpuSearchEngine.cs`
- Modify: any host caller of `k_match_records` (find with `grep -rn "k_match_records" src tests`)
- Test: `tests/XrplVanity.Tests/Gpu/GpuMatcherTests.cs`

**Interfaces:**
- Consumes: `TargetTable.Pairs`, `PairCount` from Task 3.
- Produces: `matches_any(body, bodyLen, groups, groupCount, chars, pairs, pairCount)`; `k_match_records(records, n, groups, groupCount, chars, pairs, pairCount, flags)`; `k_search(…, groups, groupCount, chars, pairs, pairCount, hits, hitCount, hitCapacity, table)` — keep the new parameters immediately after `chars` in both, so the two call sites read the same.

- [ ] **Step 1: Write the failing test**

Add to `GpuMatcherTests` a cross-check that runs on both backends:

```csharp
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void The_Device_Agrees_With_The_Cpu_On_Paired_Patterns(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "yz", ignoreCase: false)
            .AddPair("cd", "wx", ignoreCase: true)
            .Add(MatchPosition.Starts, "qq", ignoreCase: false)
            .Build();

        // A corpus with every interesting case: both halves, one half, neither, and bodies too
        // short to hold both halves.
        List<string> addresses = new()
        {
            "rabQQQQQQQyz", "rabQQQQQQQQQ", "rQQQQQQQQQyz", "rQQQQQQQQQQQ",
            "rCDQQQQQQQWx", "rcdQQQQQQQwx", "rqqQQQQQQQQQ", "rabyz", "rab", "ryz",
        };
        Random random = new(20260906);
        for (int i = 0; i < 200; i++)
        {
            addresses.Add(GpuTestHelper.RandomAddress(random));   // add this helper if it does not exist
        }

        bool[] deviceFlags = GpuTestHelper.MatchRecords(device, set, addresses);   // wraps k_match_records
        for (int i = 0; i < addresses.Count; i++)
        {
            bool cpu = set.TryMatch(addresses[i], out _, out _);
            Assert.Equal(cpu, deviceFlags[i]);
        }
    }
```

If `GpuMatcherTests` already has a helper that uploads a table and runs `k_match_records`, use
it and extend it with the pairs buffer instead of adding `GpuTestHelper.MatchRecords`; keep one
helper, not two. `RandomAddress` builds `'r'` plus 24 to 34 characters drawn from
`XrplBase58.Alphabet`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"`
Expected: a compile error on the new parameters, or a mismatch, before the kernel changes.

- [ ] **Step 3: Change the kernel**

In `gpu/kernels/xrpl_vanity_body.h`, in `matches_any`: `continue` when `pos` is 3 or 4 in the
existing or-loop, then add the pair loop from the spec. Extend the two kernel signatures.
Update the comment block above `matches_any` to describe the pairs buffer and the pair-only
codes.

- [ ] **Step 4: Rebuild the PTX**

```bash
python gpu/build-ptx.py
```

Expected: `wrote …/xrpl_vanity.ptx (… bytes, compute_75, body-sha256 …)` with a **new** hash.
`git diff --stat gpu/kernels/xrpl_vanity.ptx` must show the file changed.

- [ ] **Step 5: Update the host**

`GpuSearchEngine`: allocate `_pairs = device.Alloc(Math.Max(1, table.Pairs.Length * 4))`, copy
the buffer (`CopyFrom` on the byte view — follow exactly how `_groups` is filled), and pass
`_pairs, table.PairCount` to the launch in the same position as in the kernel. Dispose it with
the others. Update any other caller of the two kernels.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests" -class "*KernelSourceTests" -class "*GpuSearchTests"`
Expected: all pass on both backends, `KernelSourceTests` included (it re-derives the stamp).

Then the whole suite once: `dotnet run --project tests/XrplVanity.Tests -c Release` — 0 failed, 0 skipped.

- [ ] **Step 7: Commit**

```bash
git add gpu/kernels src/XrplVanity.Gpu tests/XrplVanity.Tests/Gpu
git commit -m "feat(gpu): the kernel requires both halves of a paired pattern"
```

---

### Task 5: `--starts-ends` in the CLI

**Files:**
- Modify: `src/XrplVanity.Cli/CliOptions.cs`
- Modify: `src/XrplVanity.Cli/Program.cs` (`SearchRunner.BuildPatternSet` and the usage text if one exists)
- Modify: `linux/in-container.sh`
- Test: `tests/XrplVanity.Tests/CliOptionsTests.cs`, `tests/XrplVanity.Tests/CliRuntimeTests.cs`

**Interfaces:**
- Produces: `CliOptions.Pairs` (`IReadOnlyList<(string Starts, string Ends)>`); the option `--starts-ends PREFIX:SUFFIX`, repeatable; the usage error text `Option '--starts-ends' needs PREFIX:SUFFIX, for example abc:xyz.`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Parses_Starts_Ends_Pairs()
    {
        CliOptions options = CliOptions.Parse(new[] { "--starts-ends", "abc:xyz", "--starts-ends", "q:w" });
        Assert.Equal(2, options.Pairs.Count);
        Assert.Equal(("abc", "xyz"), options.Pairs[0]);
        Assert.Equal(("q", "w"), options.Pairs[1]);
    }

    [Theory]
    [InlineData("abcxyz")]
    [InlineData(":xyz")]
    [InlineData("abc:")]
    [InlineData("a:b:c")]
    public void A_Malformed_Starts_Ends_Value_Is_A_Usage_Error(string value)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { "--starts-ends", value }));
        Assert.Contains("--starts-ends", error.Message);
    }

    [Fact]
    public void BuildPatternSet_Turns_A_Pair_Option_Into_A_Paired_Rule()
    {
        CliOptions options = CliOptions.Parse(new[] { "--case-sensitive", "--starts-ends", "ab:yz" });
        PatternSet set = SearchRunner.BuildPatternSet(options);
        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);
    }
```

And in `CliRuntimeTests`, a deterministic run: build a `PatternSet` through the option, feed
`SearchRunner.SearchDeterministic` an entropy stream whose second element is known to derive an
address satisfying a very short pair (use a one-character prefix and a one-character suffix so a
handful of entropies suffice — search for the entropy by brute force **inside the test** over at
most a few thousand deterministic values, or reuse the existing fixed-seed fixture if one
matches).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliOptionsTests" -class "*CliRuntimeTests"`

- [ ] **Step 3: Implement**

`CliOptions`: a `List<(string, string)> _pairs` behind `IReadOnlyList<(string Starts, string Ends)> Pairs`; a `case "--starts-ends":` that takes the next value, splits on `':'`, requires exactly two non-empty halves after trimming and otherwise throws `ArgumentException` with the message above. `BuildPatternSet` calls `builder.AddPair(starts, ends, ignoreCase: !options.CaseSensitive)` for each. If the CLI prints a usage/help block, add the option row there in the style of the others.

`linux/in-container.sh`: after the existing `--gpu` smoke, add one that runs
`--starts-ends a:b --count 1` on the GPU and checks exit 0 and an `Address:` line, using the
file's existing `check` helper and capture pattern.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliOptionsTests" -class "*CliRuntimeTests" -class "*CliProgramTests"`

Then a real command, and paste its output into the report:

```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts-ends fa:st --count 1
```

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Cli linux/in-container.sh tests/XrplVanity.Tests
git commit -m "feat(cli): --starts-ends searches for a prefix and a suffix together"
```

---

### Task 6: the desktop application's fourth entry mode

**Files:**
- Modify: `src/XrplVanity.App.Core/ViewModels/PatternChip.cs`, `SearchViewModel.cs`
- Modify: `src/XrplVanity.App.Core/Components/PatternEditor.razor` (+ `.razor.cs`, `.razor.css`)
- Test: `tests/XrplVanity.App.Tests/PatternEditorTests.cs`, `SearchViewModelTests.cs`

**Interfaces:**
- Consumes: `PatternSet.Builder.AddPair`, `MatchPosition.Pair`.
- Produces: `PatternChip.EndsText` (`string?`, the second half, null for a plain chip); `SearchViewModel.EntryEndsText` (the second box); `AddPattern` unchanged for plain patterns plus `AddPair(string starts, string ends, bool ignoreCase)`; the entry mode `MatchPosition.Pair` in `EntryPosition`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void The_Fourth_Mode_Shows_Two_Boxes_And_Adds_A_Paired_Chip()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Pair");
        Assert.Single(cut.FindAll("input.pattern"));
        Assert.Single(cut.FindAll("input.pattern-ends"));

        cut.Find("input.pattern").Input("abc");
        cut.Find("input.pattern-ends").Input("xyz");
        cut.Find("input.pattern-ends").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("abc", chip.Text);
        Assert.Equal("xyz", chip.EndsText);
        Assert.Equal(MatchPosition.Pair, chip.Position);
        Assert.Contains("abc", cut.Find(".chip").TextContent);
        Assert.Contains("xyz", cut.Find(".chip").TextContent);
    }

    [Fact]
    public void A_Paired_Chip_Warns_About_The_Half_That_Cannot_Occur()
    {
        SearchViewModel vm = Vm();
        vm.AddPair("abc", "@", ignoreCase: false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("cannot occur", cut.Find(".chip-warn").TextContent);
    }
```

Plus in `SearchViewModelTests`: `AddPair` adds a chip and rebuilds the set; `ExpectedCandidates`
for `abc`/`xyz` lands between 1.5e10 and 1.6e10; a paired rule alone makes `CanStart` true; the
pending two-box entry is committed by `Start()` the way a single box already is.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*PatternEditorTests" -class "*SearchViewModelTests"`

- [ ] **Step 3: Implement**

`PatternChip` gains `EndsText`. `SearchViewModel` gains `EntryEndsText` (raising `Changed` like
the others), `AddPair`, and in `RebuildPatternSet` calls `builder.AddPair(chip.Text, chip.EndsText!, chip.IgnoreCase)`
for a chip whose `Position` is `Pair`, computing its `Warning` from both halves (the first half
that is unreachable wins, with the same wording as today). `AddPatternFromEntry` commits the two
boxes when the mode is `Pair` and both are non-blank; the pending-entry hint and
`PatternProblem` treat the pair as reachable only when both halves are.

`PatternEditor`: the position `<select>` gains `<option value="Pair">starts and ends with</option>`;
when `Vm.EntryPosition == MatchPosition.Pair` the row renders two inputs (`input.pattern` bound to
`Vm.EntryText` with placeholder `starts with…`, `input.pattern-ends` bound to `Vm.EntryEndsText`
with placeholder `ends with…`), both handling Enter. A paired chip renders
`<span class="chip-pos">starts</span> <span class="mono">@chip.Text</span> <span class="chip-pos">ends</span> <span class="mono">@chip.EndsText</span>`.
Keep the narrow-window rule working: the two-input row uses the same `pattern-entry` grid with an
extra column, and below 1000 px it wraps like the rest.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release` — 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): the pattern editor can require a prefix and a suffix together"
```

---

### Task 7: documentation and measurements

**Files:**
- Modify: `README.md`, `docs/benchmarks.md`, `docs/desktop-app.md`
- Modify: `docs/2026-09-06-xrpl-vanity-paired-patterns-design.md` (a "Measured" section)

- [ ] **Step 1: Measure**

With everything built in Release and nothing else CPU-heavy running:

```bash
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"
```

Record the throughput line for both backends and compare it with the figures in
`docs/benchmarks.md` (cuda 19.3 M/s, opencl 21.0 M/s). Then time a real paired search:

```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --case-sensitive --starts-ends ab:yz --count 3
```

and record its rate line. Never paste a seed anywhere.

- [ ] **Step 2: Write the documentation**

- `README.md`: the `--starts-ends PREFIX:SUFFIX` row in the options table, and one sentence in
  the pattern section: a prefix of *m* characters plus a suffix of *n* costs about as much as a
  single prefix of *m + n*, because the cost is set by the number of constrained characters.
- `docs/benchmarks.md`: the same cost rule beside "Expected candidates per match", with the
  worked example `abc` + `xyz` = 15.3 G.
- `docs/desktop-app.md`: the fourth entry mode in the pattern section, and a checklist line.
- The design document: a "### Measured" section with the throughput before and after and the
  paired search's rate.

- [ ] **Step 3: Validate and commit**

```bash
PYTHONIOENCODING=utf-8 python "C:/Users/Evril/.claude/skills/developer-documentation/scripts/validate_docs.py" README.md docs/benchmarks.md docs/desktop-app.md docs/2026-09-06-xrpl-vanity-paired-patterns-design.md
git add README.md docs
git commit -m "docs: paired patterns, their cost model and the measured throughput"
```

---

## Self-review

- **Spec coverage.** Model and `TryMatch` order → Task 1; estimate → Task 2; table → Task 3; kernel, PTX and host → Task 4; CLI and harness → Task 5; application → Task 6; documentation and measurements → Task 7.
- **Placeholders.** None: every step carries the code or the exact command.
- **Type consistency.** `PatternSet.Pair` (Task 1) is what Tasks 2, 3 and 6 consume; `TargetTable.Pairs`/`PairCount` (Task 3) is what Task 4 uploads; `MatchPosition.Pair` is used by Tasks 1, 5 and 6; `PatternChip.EndsText` (Task 6) is only used inside the application.
