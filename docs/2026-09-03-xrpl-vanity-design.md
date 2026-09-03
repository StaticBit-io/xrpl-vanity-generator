# XRPL Vanity Address Generator (.NET) — Design

Status: approved 2026-09-03. This is the v1 design for a standalone .NET CLI that
generates XRP Ledger vanity addresses, and the correctness reference ("etalon")
for a later GPU (CUDA) implementation.

## Background

The starting point is `nhartner/xrp-vanity-address` (Apache-2.0), a Java tool
compiled to a GraalVM native image. Measured on an i5-11400F (6c/12t) it sustains
~39,000 addresses/second. A throwaway .NET 10 prototype of the same pipeline
reached ~138,000 addresses/second — ~3.5x — with the gap coming from per-candidate
overhead (base58 seed encode + double decode, hex round-trips, stream
allocations), not from the language. A GPU field-arithmetic spike measured
~3.2e10 field multiplications/second on an RTX 5070 Ti, implying a realistic
2–20 million addresses/second once a correct Ed25519 kernel exists. The GPU work
is a separate, later project; this document covers only the CPU reference.

## Goals

1. A correct, portable CPU generator that is trivial to reason about and verify.
2. Match semantics that a future GPU kernel can reproduce bit-for-bit, so this
   implementation can serve as the correctness reference.
3. Performance in the same order as the prototype (~10^5 addr/s on a desktop).

## Non-goals (v1)

- secp256k1 keys (Ed25519 only).
- Full regular-expression matching.
- The GPU/CUDA kernel.
- The one-off benchmark harness (stays in scratch, not in the repo).

## Repository layout

New public repository, prepared locally at `E:\GIT\XRPL\xrpl-vanity`. Published
only on explicit command. All repository artifacts (code, docs, commits) are in
English.

```
xrpl-vanity/
  src/XrplVanity.Core/    # library: address derivation, matcher, pattern normalization
  src/XrplVanity.Cli/     # CLI: argument parsing, threading, output
  tests/XrplVanity.Tests/ # xUnit v3: known vectors + cross-check + matcher tests
  docs/                   # this design
```

The Core library holds no threading and no I/O — pure functions `entropy ->
address` and `address -> match?`. These two functions are exactly what the GPU
kernel must later reproduce.

## Technology

- .NET 10, C# latest, `<Nullable>enable</Nullable>`, `AllowUnsafeBlocks` for
  span-based hashing.
- Crypto backend: **BouncyCastle.Cryptography** (managed, Apache-2.0). Chosen over
  libsodium because the ~9% single-thread speed cost is outweighed, for a
  reference implementation, by having no native dependency and identical behavior
  on every platform. The backend sits behind an interface so a libsodium backend
  can be added later alongside the GPU work.
- Test framework: xUnit v3 with the built-in `Assert` (no FluentAssertions).

## Per-candidate pipeline

All work is span-based with no per-candidate heap allocation, as validated in the
prototype:

```
CSPRNG 16 bytes
  -> SHA-512                    (entropy -> Ed25519 seed material)
  -> Ed25519 public key (0xED || 32 bytes)
  -> SHA-256
  -> RIPEMD-160                 (20-byte AccountID)
  -> base58check (AccountID version 0x00, 4-byte double-SHA-256 checksum)
  = classic address rXXXX...
```

### Entropy

Full CSPRNG entropy (`RandomNumberGenerator`) per candidate, drawn in batches into
a reusable buffer. The original Java tool mutated only the low byte of the entropy
(`% 64`) to amortize RNG cost, producing 64 near-identical seeds per random draw;
that weakens the generated key and is not reproduced here. In the prototype the
CSPRNG was not the bottleneck at ~10^5 addr/s, so full entropy costs nothing
practical.

### Seed encoding

The `sEd...` family seed is produced only for a matched candidate (payload
`0x01 0xE1 0x4B` || 16 entropy bytes, base58check), never in the hot loop.

## Matcher

Positions: `starts` (default), `ends`, `contains`. Multiple patterns per run;
a candidate matches if any pattern matches at its position.

Storage: per-position, patterns are bucketed by length into `HashSet<string>`
(`StringComparer.Ordinal`); lookups use
`HashSet<string>.GetAlternateLookup<ReadOnlySpan<char>>()` so matching against the
address slice allocates nothing. This is the mechanism that let the prototype run
a 20,000-word list at essentially the same rate as three words.

The address is matched excluding the leading `r`, mirroring the original tool
(the vanity substring starts at index 1).

### Case handling

Matching is case-insensitive by default. This is implemented by expanding each
pattern at build time into *every* reachable case combination — the Cartesian
product of each character's alphabet-legal cases — and inserting them all into the
sets, so the hot loop stays a plain ordinal set lookup. A character that exists in
only one case in the base58 alphabet (`i`, `o` lower-only; `l` as `L`) contributes
a single choice, so the count is `2^(two-case letters)`, not `2^length`.

To bound the expansion, case-folding applies only to patterns up to
`PatternSet.MaxIgnoreCaseLength` (12) characters. A longer pattern is still
searched but case-sensitively, and is surfaced via `PatternSet.CaseFoldingSkipped`
so the CLI can warn. `--case-sensitive` inserts the pattern as given.

(The original Java tool expanded only the upper-case and title-case forms, which
missed most mixed-case combinations; this SDK covers them all under the length
cap.)

### Pattern normalization into the base58 alphabet

The XRPL base58 alphabet
`rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz` excludes `0 O I l`.
User patterns are normalized (`l->L`, `0->o`, `O->o`, `I->i`) as the original did,
**but with a visible notice**: for each pattern the tool prints what it was
normalized to, and if a pattern contains a character that cannot occur in an
address and cannot be normalized, it reports that pattern as unreachable instead
of searching forever.

## CLI surface

```
xrpl-vanity ripple hodl xrp            # targeted words (default position: starts)
xrpl-vanity --list google20kwords 5    # roulette: words from file, min length 5
xrpl-vanity --ends xyz --contains dao  # suffix / anywhere
```

Flags:

Each of `--starts`, `--ends`, `--contains` takes one or more patterns and tags
them with that position; bare positional words go into the `starts` bucket. A run
may mix positions (`--ends xyz --contains dao`). A candidate matches if any pattern
matches at its tagged position.

| Flag | Default | Meaning |
| --- | --- | --- |
| positional words | — | patterns for the `starts` bucket |
| `--starts P...` | — | patterns to match at address start (after `r`) |
| `--ends P...` | — | patterns to match at address end |
| `--contains P...` | — | patterns to match anywhere |
| `--list FILE [minLen]` | — | load `starts` patterns from a word list, optional min length filter |
| `--case-sensitive` | off (case-insensitive) | match patterns exactly; default expands case variants |
| `--threads N` | `ProcessorCount` | worker threads (fixes the original's hard-coded 16) |
| `--out FILE` | — | append matches to a file as well as stdout |
| `--count N` | — | stop after N matches (otherwise run until interrupted) |

### Threading

`ProcessorCount` worker threads by default, each with its own deriver and RNG
buffer, lock-free running counters, a status line with the aggregate rate. No
shared mutable state on the hot path.

### Secret handling

A seed is a secret: whoever holds it controls the account. The tool prints a
one-time warning, writes matches to stdout (and `--out` if given), sends nothing
anywhere, and has no telemetry.

## Correctness strategy

This implementation is the reference, so correctness is pinned explicitly:

1. **Known vector** from the original binary:
   `sEdTuRgwYiEcubPJfRJUArVyn7AXkVj -> rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK`
   (already reproduced by the prototype).
2. **Cross-check** against the `XrplCSharp` SDK (`Xrpl.Keypairs`): for a batch of
   random seeds, the address this tool derives must equal the SDK's.
3. **base58 seed round-trip**: encode(entropy) -> decode -> entropy.
4. **Matcher and normalization** unit tests, including unreachable-character
   patterns and each position (`starts`/`ends`/`contains`).

## Expected result

Roughly the prototype's ~138,000 addr/s on the reference machine (BouncyCastle a
few percent below libsodium's peak, same order of magnitude): ~3.5x over the
current binary, plus a clean reference core the GPU kernel can be verified
against.
