# xrpl-vanity

A multithreaded command-line tool that searches for XRP Ledger (XRPL) accounts
whose address contains a chosen substring — a "vanity address" generator.

It repeatedly generates 16 bytes of cryptographically secure entropy, derives
the corresponding seed and address, and checks the address against your
patterns. When a pattern matches, it prints the address and the seed that
produced it.

**Ed25519 only.** XRPL also supports secp256k1 accounts, but this tool always
derives Ed25519 keys (seeds encoded with the `sEd...` prefix). If you need a
secp256k1 vanity address, this is not the tool for that.

## Building and running

Requires the .NET 10 SDK.

```bash
dotnet build -c Release
dotnet run --project src/XrplVanity.Cli -c Release -- --contains xrp --count 1
```

## Match modes

Patterns can be anchored to the start of the address (right after the leading
`r`), the end, or anywhere inside it. Bare positional words default to
`starts`.

```bash
# targeted words — default position is "starts"
xrpl-vanity ripple hodl xrp

# roulette — patterns loaded from a word list, minimum length 5
xrpl-vanity --list google20kwords 5

# suffix / anywhere — mix positions in a single run
xrpl-vanity --ends xyz --contains dao
```

A run may combine `--starts`, `--ends`, and `--contains` together, and a
candidate address matches if *any* pattern matches at its tagged position.

## Flags

| Flag | Default | Meaning |
| --- | --- | --- |
| positional words | — | patterns for the `starts` bucket |
| `--starts P...` | — | patterns to match at address start (after `r`) |
| `--ends P...` | — | patterns to match at address end |
| `--contains P...` | — | patterns to match anywhere |
| `--list FILE [minLen]` | — | load `starts` patterns from a word list, optional min length filter (default 4) |
| `--case-sensitive` | off (case-insensitive) | match patterns exactly; default expands case variants |
| `--threads N` | `ProcessorCount` | number of worker threads |
| `--out FILE` | — | append matches to a file as well as stdout |
| `--count N` | — | stop after N matches (otherwise run until interrupted, e.g. Ctrl+C) |

Patterns are normalized into the XRPL base58 alphabet (`l -> L`, `0 -> o`,
`O -> o`, `I -> i`), matching how the original tool behaved. A pattern that
still contains a character that cannot occur in an address (outside the
base58 alphabet, even after normalization) is reported as unreachable and
skipped instead of searching forever.

## Secret handling

**A seed is a secret.** Anyone who holds it controls the account. Before
starting a search, the tool prints a one-time warning to this effect. Matches
are written to stdout (and to `--out FILE` if given) and nowhere else — the
tool has no network access and no telemetry. Treat every seed it prints as
sensitive: keep matches offline, and verify this tool yourself before using a
generated key to hold real funds.

## Performance

The search runs on `Environment.ProcessorCount` threads by default (override
with `--threads`), each with its own key deriver and CSPRNG-filled entropy
buffer, so the hot loop has no shared mutable state beyond a lock-free match
counter.

**Planned: GPU backend.** The address-derivation and pattern-matching core
(`XrplVanity.Core`) is deliberately kept free of CLI and threading concerns so
its semantics — `entropy -> address` and `address -> match?` — can be
reproduced bit-for-bit by a future GPU (CUDA) kernel for much higher
throughput. That kernel is not implemented yet.

## Acknowledgements

This tool is inspired by, and its CLI surface and matching semantics are
modeled after, [`nhartner/xrp-vanity-address`](https://github.com/nhartner/xrp-vanity-address),
a Java XRPL vanity address generator released under the Apache License 2.0.
This is an independent C# reimplementation, not a fork.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
