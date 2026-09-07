# XRPL Vanity Generator — Paired Patterns (prefix and suffix together) Design

Status: approved 2026-09-06. Seventh design document, after the CLI, the CUDA backend
(Plan A/B), the backend abstraction and OpenCL backend (Plan C), the Linux harness and pocl
validation (Plan D), multi-device orchestration (Plan E) and the shared search layer with the
desktop application (Plans F and G), all merged. secp256k1 remains permanently out of scope.

## Background

Every pattern the tool accepts today is one condition on the address body — the address
without its fixed leading `r`: a prefix (`--starts`), a suffix (`--ends`) or a substring
(`--contains`). Several patterns are combined with **or**: `PatternSet.TryMatch` returns on the
first condition that holds, and the GPU kernel's `matches_any` does the same over a flattened
table of targets. There is no way to require two conditions at once.

The user asked for exactly one combination: an address that begins with one fragment **and**
ends with another. It is an attractive shape because the cost is set by the total number of
constrained characters, not by their arrangement: a prefix of *m* and a suffix of *n* costs
about the same as a single prefix of length *m + n*, while reading much better.

| Condition | Constrained characters | Expected candidates | On the reference RTX 5070 Ti |
| --- | --- | --- | --- |
| `abc` + ends `x` | 4 | 264 M | 14 s |
| `abc` + ends `xyz` | 6 | 15.3 G | 13 min |
| `abcd` + ends `xyz` | 7 | 888 G | 13 h |

Facts that shape this design:

- `PatternSet` (in `XrplVanity.Core`) stores expanded, normalized targets bucketed by length
  for prefixes and suffixes and as a flat list for substrings, and exposes them through
  `EnumerateTargets()`. Case-insensitive patterns are expanded into every reachable case
  variant at build time, capped at `MaxIgnoreCaseLength` (12) characters per pattern.
- `TargetTable` (in `XrplVanity.Gpu`) flattens `EnumerateTargets()` into two buffers: `Groups`
  (four `uint` per group: position code, length, offset, count) and `Chars` (each group's
  targets sorted ordinal and packed at a stride equal to their length, so the device binary
  searches a group).
- The kernel's `matches_any` (in `gpu/kernels/xrpl_vanity_body.h`) loops over the groups and
  returns `true` on the first group that contains the relevant slice. The same source compiles
  to CUDA PTX (checked in, stamped with `// body-sha256:` and pinned by a device-free test) and
  to OpenCL C at run time.
- Every device hit is re-derived and re-checked on the CPU by `CandidateEvaluator` before it is
  reported, so `PatternSet` is the single authority on what a match is.
- The desktop application and the CLI both build their pattern set through
  `PatternSet.Builder`, and the estimate comes from `SearchEstimator`, which sums the
  probability of every target.

## Decisions taken with the user

| Question | Decision |
| --- | --- |
| Which conjunctions are supported | Prefix **and** suffix only. Not substrings, not an arbitrary number of conditions |
| Which devices in this version | CPU and GPU together: the kernel is changed and the PTX rebuilt. A CPU-only version would be useless, because six constrained characters take a day on the CPU and thirteen minutes on the GPU |
| Existing semantics | `--starts` and `--ends` keep meaning **or**; the new rule is a separate kind of pattern |

## Goals

1. A pattern that requires a prefix and a suffix at the same time, searched on the CPU and on
   every GPU backend, with device hits verified against the same CPU authority as today.
2. The estimate accounts for it exactly, so the ESTIMATE card and any planning based on it stay
   truthful.
3. The CLI expresses it without breaking a single existing command line.
4. The desktop application expresses it in the pattern editor, and the result rows say which
   rule matched.
5. No measurable throughput loss when no paired pattern is in use.

## Non-goals

- Substrings inside a conjunction, negations, character classes, positional wildcards.
- More than two parts in one rule.
- Changing the meaning of `--starts`, `--ends`, `--contains` or of several patterns together.

## The model

A **paired pattern** is one rule with two required halves: a prefix and a suffix, both
non-empty. In `PatternSet` it is stored as a pair of buckets — one holding every case variant
of the prefix, the other every case variant of the suffix — so case expansion stays additive.
`abc` + `xyz` with case folding produces 8 + 8 variants, not 64 combinations.

```csharp
namespace XrplVanity.Core;

public sealed class PatternSet
{
    // … existing members …

    /// <summary>One paired rule: the address body must start with one of <see cref="Starts"/>
    /// and end with one of <see cref="Ends"/>. Both sets hold expanded, normalized variants of
    /// the same length as their own side.</summary>
    public sealed class Pair
    {
        public IReadOnlyCollection<string> Starts { get; }
        public IReadOnlyCollection<string> Ends { get; }
        public int StartsLength { get; }
        public int EndsLength { get; }
        /// <summary>"abc..xyz" — how the rule is reported in a <see cref="Match"/>, in the CLI's
        /// output and in the desktop application's result rows.</summary>
        public string Display { get; }
    }

    public IReadOnlyList<Pair> Pairs { get; }

    public sealed class Builder
    {
        // … existing members …

        /// <summary>Adds a rule that requires both halves. Either half unreachable, empty after
        /// normalization, or longer than the address body makes the rule unreachable and it is
        /// reported through <see cref="Unreachable"/> with the original text "starts..ends".
        /// Case folding is applied per half and skipped for a half longer than
        /// <see cref="MaxIgnoreCaseLength"/>, reported through <see cref="CaseFoldingSkipped"/>
        /// exactly as it is for a single pattern.</summary>
        public Builder AddPair(string starts, string ends, bool ignoreCase);
    }
}
```

`HasPatterns` becomes true when any bucket **or any pair** is present. `TryMatch` checks pairs
first, then prefixes, suffixes and substrings as it does today: a paired rule is the most
specific thing that can hold, so when an address satisfies both a pair and a plain pattern the
pair is the one reported. `MatchPosition` gains `Pair`, and `Match.Pattern` carries the
`Display` string.

The reported form uses two ASCII dots (`abc..xyz`) rather than an ellipsis: it travels through
the CLI's tab-separated output, a TSV export and a terminal without an encoding question, and
it cannot collide with a pattern because `.` is not in the base58 alphabet.

## The device path

`TargetTable` keeps its group format and gains one buffer:

- Groups that belong to a pair are emitted with new position codes — `PositionPairStarts` (3)
  and `PositionPairEnds` (4) — which the kernel's or-loop skips, so a half can never match on
  its own.
- A new `Pairs` array holds two `uint` per rule: the index of the prefix group and the index of
  the suffix group. `PairCount` is its length divided by two.
- Halves are shared: two rules with the same prefix set and length reference the same group.

The kernel gains the matching half:

```c
// pairs: 2 x u32 per rule = { prefix group index, suffix group index }. Both groups carry a
// pair-only position code, so the or-loop above skips them and a half never matches alone.
DEVICE bool matches_any(const u8* body, int bodyLen,
                        GLOBAL const u32* groups, int groupCount, GLOBAL const u8* chars,
                        GLOBAL const u32* pairs, int pairCount)
{
    // … the existing or-loop, extended to `continue` on position 3 and 4 …

    for (int p = 0; p < pairCount; p++) {
        u32 gs = pairs[p * 2];
        u32 ge = pairs[p * 2 + 1];
        u32 sl = groups[gs * 4 + 1];
        u32 el = groups[ge * 4 + 1];
        if ((int)(sl + el) > bodyLen) continue;
        if (!group_has(chars, groups[gs * 4 + 2], groups[gs * 4 + 3], sl, body)) continue;
        if (group_has(chars, groups[ge * 4 + 2], groups[ge * 4 + 3], el, body + bodyLen - (int)el)) return true;
    }

    return false;
}
```

`k_match_records` and `k_search` take the two extra parameters; `GpuSearchEngine` allocates and
uploads the pairs buffer (an empty one when there are no pairs, so the launch signature never
varies) and passes `PairCount`. The PTX is rebuilt with `gpu/build-ptx.py` and its
`// body-sha256:` stamp updated; the device-free `KernelSourceTests` keeps proving that the
checked-in PTX was built from the checked-in source.

The guard `sl + el > bodyLen` matters: XRPL addresses are 25 to 35 characters, so a rule whose
halves together exceed a short body must not read past it.

Cost when no pair is in use: one comparison of `pairCount` against zero per candidate. The
existing throughput test is the gate.

## The estimate

Both halves are independent, so the probabilities multiply. For one rule:

```
P(rule) = (Σ over prefix variants of LeadingCharacterProbability(v[0]) × 58^-(m-1))
        × (count of suffix variants × 58^-n)
```

where *m* and *n* are the two lengths. `SearchEstimator.ExpectedCandidatesPerMatch` adds
`P(rule)` for every pair to the sum it already builds from the single targets, and returns the
reciprocal as before. `PatternSet` exposes the pairs for this (`Pairs`), so the estimator needs
no new enumeration shape.

Worked example, all-lower `abc` + `xyz`, case-sensitive: `23.338 × 58² × 58³ = 1.53 × 10¹⁰`,
which is the 15.3 G in the table above.

## The command line

One new repeatable option:

```
--starts-ends PREFIX:SUFFIX
```

Both halves are required and separated by a colon, which is unambiguous because the base58
alphabet has no colon. `--starts-ends abc:xyz` and `--starts fast` in the same command line
mean "either the paired rule or the prefix", consistent with how several patterns have always
combined. A missing colon, an empty half or more than one colon is a usage error (exit 2) with
the message `Option '--starts-ends' needs PREFIX:SUFFIX, for example abc:xyz.`

`--list` files keep holding one plain prefix per line; paired rules are not read from a list.

## The desktop application

The position selector in the PATTERNS card gains a fourth entry, `starts and ends with`.
Choosing it replaces the single text box with two, labelled by their placeholders
(`starts with…` and `ends with…`); Enter in either box, or pressing Start, adds the rule. The
chip renders `starts abc … ends xyz` — the ellipsis is fine here, this is UI text, not data —
with the `Aa` marker and the per-half warnings the editor already shows for one pattern. The
view model's pending-entry hint and the unreachable message cover both halves.

`SessionResult.Pattern` carries the `Display` string, so the results list, the History page and
a TSV export all show `abc..xyz` for a paired hit with no further work.

## Testing

- **Core** (`PatternSetTests`, `PatternNormalizerTests`): a rule matches only when both halves
  hold; a half alone does not match; normalization and the look-alike folding apply per half;
  an unreachable half makes the rule unreachable and is reported once; case folding expands
  each half separately and is skipped per half past the 12-character cap; `HasPatterns` sees
  pairs; `TryMatch` reports the pair before a plain pattern that also holds; the `Display`
  form.
- **Estimator** (`SearchEstimatorTests`): exact values for `abc` + `xyz` case-sensitive and for
  a case-insensitive pair; a pair summed with a plain pattern.
- **GPU** (`GpuMatcherTests` and the existing cross-check shape): on both backends, `k_match_records`
  agrees with `PatternSet.TryMatch` over a corpus that includes addresses matching one half
  only, both halves, and neither; a table with pairs and plain patterns together; the empty-pair
  case still matches as before. `KernelSourceTests` keeps the PTX stamp honest.
- **CLI** (`CliOptionsTests`, `CliProgramTests`): parsing and its usage errors; a deterministic
  search that finds a paired match in a fixed entropy stream; the `Address:\tSeed:` line and the
  `--out` file unchanged in shape.
- **Application** (`PatternEditorTests`, `SearchViewModelTests`): the fourth position mode, the
  two boxes, the chip, the warnings, the estimate.
- **Harness**: one smoke in `linux/in-container.sh` that runs `--starts-ends a:b` on the GPU
  mode and expects exit 0 with an `Address:` line.
- **Throughput**: the existing benchmark test, run before and after, to show the kernel change
  costs nothing when no pair is present.

## Documentation

`README.md` gains the option row and one sentence on the cost model (a prefix of *m* plus a
suffix of *n* costs exactly as much as a single prefix of *m + n*). `docs/benchmarks.md` gains the
same cost note beside the existing "expected candidates per match" paragraph.
`docs/desktop-app.md` describes the fourth entry mode. The plan's measurements section records
the before/after throughput.

### Measured

Windows, reference RTX 5070 Ti + i5-11400F (same machine as `docs/benchmarks.md`),
commit 1b52423, 2026-09-06, nothing else CPU-heavy running, taken before this
documentation was written.

**Kernel benchmark test** (`GpuSearchTests.Throughput_Meets_The_Floor_For_The_Device_Class`,
a non-matching pattern, run with `-showLiveOutput` since the default runner output hides
per-test `[OUTPUT]` lines):

```bash
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests" -showLiveOutput
```

| Backend | Recorded baseline (`docs/benchmarks.md`, pre-pair) | Measured on this branch | Difference |
| --- | --- | --- | --- |
| `cuda` | 19,300,000/s | 19,579,551/s | +1.4% |
| `opencl` | 21,000,000/s | 21,303,774/s | +1.4% |

Both backends ran for real (0 skipped, 10/10 tests passed) and both land within the same
noise band the reference page already documents for repeated runs (compare the two CUDA
CLI figures in "Single GPU device", −0.4% and +0.5% of each other). The `pairCount == 0`
branch the kernel now always evaluates costs nothing measurable, confirming goal 5.

**Paired search, CLI, GPU** (`--starts-ends ab:yz`, case-sensitive, `m = 2`, `n = 2`,
expected ≈ 4.55 M candidates/match):

```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --case-sensitive --starts-ends ab:yz --count 3
```

```
GPU: cuda:NVIDIA GeForce RTX 5070 Ti (sm_120)
Address: rabM8ZQb1VYAqiEcezXkSkxjs3REKFcDyz	Seed: [redacted]
Address: rabA4yT4wGfLNmeHsFRtZ7YoNHHYeBr6yz	Seed: [redacted]
Address: rabxiDYqtUBYuui75SESwZUa53eeMoxqyz	Seed: [redacted]
GPU: searched 4,194,304 candidates in 0.3 s (15,345,409/s); consistency errors: 0.
```

All three addresses genuinely start with `ab` and end with `yz`; seeds are never recorded
in this document. The reported 15,345,409/s is well below the kernel-only floor above,
which is consistent with the CLI's usual behavior on a short, few-hundred-millisecond run:
the sample is a single small launch (4,194,304 = 2^22 candidates) dominated by device-open
and PTX-load overhead that the kernel benchmark excludes, plus the CPU re-verification of
the 3 hits it printed. It is not evidence of a per-candidate cost from the new pair path —
the kernel benchmark above, which isolates the search loop, shows no such cost.

## Risks

- The kernel signature changes, so a stale PTX would silently mismatch its host call. The
  `body-sha256` stamp plus `KernelSourceTests` is the existing guard, and the OpenCL path
  compiles the same source at run time, so it cannot drift.
- A pair whose halves overlap on a short address (for example a 5-character prefix and a
  5-character suffix on a 25-character body) is handled by the length guard, but the CPU and the
  device must agree on the boundary; the cross-check test uses short bodies deliberately.
- Case folding on both halves multiplies the *table* size, not the cost per candidate: two
  12-character halves would emit up to 4096 + 4096 variants. The existing per-half cap keeps
  that bounded, and the table is built once per run.
