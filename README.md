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
| `--case-sensitive` | off (case-insensitive) | match the pattern exactly as typed; by default every case combination is matched |
| `--threads N` | `ProcessorCount` | number of worker threads (CPU path only; ignored with `--gpu`) |
| `--out FILE` | — | append matches to a file as well as stdout |
| `--count N` | — | stop after N matches (otherwise run until interrupted, e.g. Ctrl+C) |
| `--gpu` | off | search on the GPU (CUDA preferred, then OpenCL; every GPU hit is re-verified by the CPU before it is printed) |
| `--backend NAME` | — | force the GPU backend: `cuda` or `opencl` (case-insensitive; implies `--gpu`; combine with `--device` to pick a specific one) |
| `--device N` | — | pick a device by index: within the chosen `--backend`, or (without `--backend`) into the flat list `--list-devices` prints (implies `--gpu`) |
| `--list-devices` | — | list every device from every backend, one per line, and exit (`0` if any device was found, `2` otherwise) |

Patterns are normalized into the XRPL base58 alphabet (`l -> L`, `0 -> o`,
`O -> o`, `I -> i`). A pattern that still contains a character that cannot
occur in an address (outside the base58 alphabet, even after normalization)
is reported as unreachable and skipped instead of searching forever.

### Case-insensitive matching

By default (no `--case-sensitive`), a pattern matches **every** reachable
case combination: `--starts ab` matches an address body starting with `ab`,
`aB`, `Ab`, or `AB`. Letters that exist in only one case in the base58
alphabet do not multiply the count — `i` and `o` are lower-case only and
`l` only exists as `L`, so `oil` has the single reachable form `oiL`.

To keep the number of combinations bounded, case expansion applies only to
patterns of at most 12 characters (up to 2^12 combinations). A longer
pattern is still searched, but case-sensitively — the tool prints a warning
saying so. Pass `--case-sensitive` to match any pattern exactly as typed.

## Exit codes

`0` — a `--count` target was fully satisfied. `2` — invalid usage (unknown
or malformed option, or no reachable pattern). `130` — the run was ended by
Ctrl+C, or a `--count`-less search was interrupted, before a `--count`
target was met.

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

The address-derivation core (`XrplVanity.Core`) is deliberately kept free of
CLI and threading concerns so its semantics — `entropy -> address` — can be
reproduced bit-for-bit by a GPU backend for much higher throughput. `--gpu`
runs the search on the device. The host builds the same pattern set the CPU
path uses and uploads it; the kernel derives and matches candidates on the
GPU; every reported hit is re-derived by the CPU reference and printed only
if it agrees — this gate applies to every backend, so a device the CPU
reference disagrees with only costs throughput (reported as a consistency
error), never a wrong key. Without a usable GPU device `--gpu` exits with
code 2 rather than silently using the CPU.

## GPU backends (CUDA and OpenCL)

Two backends implement the same kernel source: CUDA (NVIDIA only, the
longest-verified path) and OpenCL. `--gpu` without `--backend` opens the
first usable device in preference order — CUDA, then OpenCL. CUDA is tried
first because its program ships precompiled (PTX, no run-time compilation
and no dependence on the vendor's OpenCL compiler — a cold OpenCL compile
takes 30–40 s on the reference card). On the reference RTX 5070 Ti, the
OpenCL backend measured slightly faster (OpenCL ~12.1M/s vs. CUDA ~10.7M/s
in CLI runs), so pass `--backend opencl` to select it, optionally with
`--device N` to pick a specific device within it; run `--list-devices` first
to see what is available and at which flat index (`--device N` without
`--backend` indexes that flat list). The OpenCL backend compiles the kernel
source at run time, in the device's constructor — the first run can take
tens of seconds while the vendor driver JIT-compiles; the CLI prints a
`note: compiling the OpenCL program took N s` line whenever that exceeds a
second, and the driver's on-disk cache makes subsequent runs fast. The
OpenCL backend has been validated on NVIDIA's OpenCL implementation only;
AMD and Intel ICDs are expected to work (OpenCL C 1.2 is a portable
standard) but are untested here — exercising a non-NVIDIA implementation is
Plan D.

### Kernel single-source discipline

There is no `.cu` file and no `#ifdef`. The device code is one body,
`gpu/kernels/xrpl_vanity_body.h`, plus two shims that each define the same
set of macros for their platform: `gpu/kernels/cuda_shim.h` (`KERNEL` →
`extern "C" __global__`, `GLOBAL`/`CONSTANT` pointers are plain pointers,
64-bit literals are a native `ull`/`ll` suffix) and `gpu/kernels/opencl_shim.h`
(`KERNEL` → `__kernel`, `GLOBAL` → `__global`, `CONSTANT` → `__constant`,
64-bit literals go through `U64_C(...)`). `gpu/build-ptx.py` concatenates the
CUDA shim with the body and compiles the result to PTX (see below);
`GpuKernels.ReadOpenClSource()` concatenates the OpenCL shim with the same
body and hands it to the vendor's OpenCL compiler at run time.

Because OpenCL C 1.2 has no generic address space, the body follows a
stricter discipline than plain CUDA would need: device functions take
private pointers only, except for the three read-only tables that are
threaded through the call tree as `GLOBAL const` (the base-point table, and
the matcher's `groups` and `chars`); kernels copy their per-thread inputs out
of `GLOBAL` memory into private arrays before calling into device functions,
and copy results back. The full construct-by-construct mapping (kernel
entry, device function, thread index, atomics, 64-bit literals, …) is in the
"Kernel single-source discipline" table in
[`docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md`](docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md).

The body implements the same `entropy -> address` derivation as
`XrplVanity.Core`, verified kernel by kernel against the .NET/BouncyCastle
reference and against RFC 8032 test vectors — CUDA and OpenCL run identical
logic, so both are checked against RFC 8032 and each other in the test
suite. The kernel uses a windowed fixed-base scalar multiplication over a
256-entry table that is built on the device at start-up by the naive
(independently verified) multiplication. On the reference RTX 5070 Ti, the
CLI achieves ~10.7M candidates/s on CUDA and ~12.1M candidates/s on OpenCL.

At development time, the CUDA half is compiled to PTX (target `compute_75`,
i.e. NVIDIA Turing / 2018 or newer — the driver JIT-compiles the PTX for the
actual GPU at load time) with:

```bash
python gpu/build-ptx.py
```

This needs the `nvidia-cuda-nvrtc` pip wheel:

```bash
pip install --user nvidia-cuda-nvrtc
```

Set `NVRTC_DIR` to override where the script looks for the NVRTC library.

The generated `gpu/kernels/xrpl_vanity.ptx` is committed to the repository
and embedded into the `XrplVanity.Gpu` assembly as a resource, so a plain
`dotnet build` never requires CUDA or NVRTC — only the NVIDIA driver is
needed at run time, to load and execute the embedded PTX. The OpenCL side
needs no build step (the body and shim are embedded as text and compiled by
the vendor driver at run time). After ANY change to `xrpl_vanity_body.h` —
or either shim — run `python gpu/build-ptx.py` and commit the body and the
regenerated PTX together; otherwise the CUDA and OpenCL backends stop
agreeing.

GPU tests (`tests/XrplVanity.Tests/Gpu`) are parameterized over both
backends and skip automatically, per backend, when that backend has no
usable device, or (CUDA) when the device is older than compute_75.

## Acknowledgements

This tool is inspired by, and its CLI surface and matching semantics are
modeled after, [`nhartner/xrp-vanity-address`](https://github.com/nhartner/xrp-vanity-address),
a Java XRPL vanity address generator released under the Apache License 2.0.
This is an independent C# reimplementation, not a fork.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
