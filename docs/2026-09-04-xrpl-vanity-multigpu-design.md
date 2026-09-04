# XRPL Vanity Generator — Multi-Device Orchestration (Plan E) Design

Status: approved 2026-09-04. Fifth GPU design document, after the CUDA backend (Plan A/B),
the backend abstraction + OpenCL backend (Plan C) and the Linux harness + pocl validation
(Plan D), all merged. Plan E is the last of the three follow-on sub-projects agreed after
Plan B (**C** → **D** → **E**). secp256k1 remains permanently out of scope.

## Background

The search runs on exactly one device: `--gpu` opens the first available device,
`--backend`/`--device` pick one explicitly, and `GpuSearchEngine` drives it in a loop from
the main thread. The CUDA backend enumerates and opens device 0 only. Everything needed to
run several devices already exists in pieces: `GpuSearchEngine` is per-device and owns
nothing shared, `MatchSink.TryReport` is lock-guarded and enforces `--count` atomically,
each engine draws a fresh 32-byte CSPRNG key per launch (so devices cannot overlap), and
`OpenClNative.SetupLock` makes concurrent device setup safe on implementations that need it.

Facts that shape this design:

- A CUDA context is bound to the thread that creates it (`CudaContext` is documented "one
  per thread"); a device must be opened on the thread that will use it.
- The reference machine has **one physical GPU**. Multi-device behaviour can be exercised
  for real in three ways: two engines on the same CUDA device; `cuda:0` + `opencl:0` on
  Windows (one card through two APIs); and, inside the Linux harness, `cuda:0` (the RTX)
  + `opencl:0` (pocl on the CPU) — two genuinely different devices. Scaling across two or
  more NVIDIA cards cannot be measured here.
- `OpenClNative.SetupLock` serialises OpenCL program builds, so opening N OpenCL devices
  costs N sequential builds at start-up (seconds each on NVIDIA with a warm cache, tens of
  seconds on pocl).

## Goals

1. Run the search on several devices at once — any mix of CUDA and OpenCL devices — with
   one shared output, one `--count` limit enforced exactly, and one Ctrl+C.
2. Enumerate and open every CUDA device, not only ordinal 0.
3. Per-device and aggregate statistics; a device that fails to open or fails mid-run is
   reported and does not stop the others.
4. The CPU re-verification gate, hit-buffer accounting and entropy handling are unchanged
   on every device.
5. Harness and documentation updated: the Linux GPU mode proves a heterogeneous run
   (RTX + pocl), the CPU-only mode proves the single-device degenerate case.

## Non-goals

- Deduplicating one physical GPU that appears under both backends (not reliably
  detectable; the operator chooses with `--gpu-devices cuda`).
- Load balancing or work stealing between devices: engines are independent generators of
  random candidates; there is nothing to balance.
- Per-device requirement in `XRPL_VANITY_REQUIRE_BACKENDS` (`cuda:1`): meaningless on a
  one-GPU reference machine; the backend-level requirement stays.
- Caching compiled OpenCL binaries to shorten N-device start-up (Plan D deferral; documented
  as a linear start-up cost).
- Measuring scaling across multiple NVIDIA cards (no hardware).
- Changing what `--gpu` alone does.

## Approach

**One worker thread per selected device, inside `XrplVanity.Gpu`, reporting into a
caller-supplied thread-safe callback.** Each worker opens its device on its own thread,
builds a `GpuSearchEngine`, and loops `RunLaunch()` until the caller's token is cancelled or
the callback signals "stop" (the `--count` limit reached). The CLI passes
`MatchSink.TryReport` as the callback and prints per-device and total statistics.

Rejected: one process per device (cannot share `--count`, duplicated start-up and output);
a single loop round-robining over devices (fights CUDA's per-thread context binding and
serialises device waits).

## Components

### CUDA enumeration for every ordinal

- `CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason)`
  — `cuInit`, `cuDeviceGetCount`, then per ordinal `cuDeviceGet`, name and compute
  capability; **no context is created**. `CudaDeviceInfo(int Ordinal, string Name, int
  Major, int Minor)`.
- `CudaContext.TryCreate(int ordinal, out CudaContext?, out string reason)`; the existing
  parameterless overload delegates with ordinal 0. An out-of-range ordinal yields the
  reason `CUDA device ordinal N is out of range (0..count-1)`. `CudaContext.Ordinal`.
- `CudaBackend.EnumerateDevices()` lists every ordinal as `GpuDeviceInfo("cuda", ordinal,
  name, "sm_XY")`; `Open(index)` opens that ordinal; out-of-range →
  `ArgumentOutOfRangeException` (so `GpuBackends.TryOpen` reports it as a reason, as today).

### `MultiDeviceSearch` (XrplVanity.Gpu)

```
DeviceSelector(string Backend, int Index)              // ToString() => "cuda:0"
DeviceSearchStats                                      // per device, filled by its worker
  Selector, DeviceName?, CandidatesSearched, ConsistencyErrors, OverflowDrops, Seconds,
  Failure? (open failure or mid-run exception message), RatePerSecond
MultiDeviceSearchResult
  Devices (IReadOnlyList<DeviceSearchStats>), DevicesOpened, TotalCandidates,
  TotalConsistencyErrors, TotalOverflowDrops, Seconds
MultiDeviceSearch(IReadOnlyList<DeviceSelector> devices, PatternSet patterns,
                  Func<Match, bool> report, Action<string>? log = null)
  internal ctor taking IReadOnlyList<IGpuBackend> for tests with fake backends
  MultiDeviceSearchResult Run(CancellationToken cancellationToken)   // blocks until every worker has finished
```

Worker (one background thread per selector, named `gpu-<selector>`):

1. `GpuBackends.TryOpen(backends, selector.Backend, selector.Index, …)` **on this thread**.
   Failure → `Failure = reason`, `log("warning: <selector> could not be opened: <reason>")`,
   worker ends. If the open took more than one second and the backend is OpenCL, `log`
   the compile-time note for that selector.
2. `using GpuSearchEngine engine = new(device, patterns)`; loop while neither the caller's
   token nor the internal stop token is cancelled: `foreach (Match m in engine.RunLaunch())
   if (!report(m)) { stop.Cancel(); break; }`; after each launch, log the same overflow and
   consistency-error deltas the single-device path logs, prefixed with the selector.
3. Any `CudaException`/`OpenClException`/other exception mid-run → `Failure = message`,
   `log("warning: <selector> stopped: <message>")`, worker ends; the others continue.
4. On exit, fill `Seconds` and the counters from the engine and dispose the device.

`Run` starts all workers, joins them all, and returns the aggregate. `report` may be
invoked from several threads concurrently — the caller supplies a thread-safe callback;
`MatchSink.TryReport` already is one, and it returns `false` at the exact call that reaches
the limit, so the limit is never exceeded regardless of how many devices race.

Entropy: unchanged — each engine generates its own fresh 32-byte CSPRNG key per launch;
two devices, or two engines on the same device, never share a PRF key.

### Selection and CLI

- New flag `--gpu-devices <spec>` (implies `--gpu`): `all` (every device of every backend,
  CUDA first), `cuda` / `opencl` (every device of that backend), or a comma-separated list
  of `backend:index` entries or flat indices as printed by `--list-devices` (e.g.
  `cuda:0,cuda:1` or `0,2`). Combining `--gpu-devices` with `--backend` or `--device` is a
  usage error (exit 2). An empty selection (e.g. `--gpu-devices cuda` on a machine without
  CUDA) → `error: --gpu-devices: no matching device (…)`, exit 2.
- `--gpu` alone stays single-device (first available). On a one-card machine `--gpu-devices
  all` runs that card through both APIs at once; `--gpu-devices cuda` is the natural "all
  my NVIDIA cards" choice — documented.
- `SearchRunner.ResolveDevices(options, out List<DeviceSelector>, out reason)`.
- `Program.RunMultiGpu`: prints `GPU[<selector>]: <device name>` as each device opens (from
  the log callback), runs `MultiDeviceSearch`, then per device
  `GPU[<selector>]: searched N candidates in T s (R/s); consistency errors: E.` (or
  `failed: <reason>`), then the total line `GPU: searched N candidates on K device(s) in
  T s (R/s); consistency errors: E.` If no device opened → `error: --gpu-devices: no device
  could be opened.`, exit 2. Otherwise the exit code rule is unchanged: 0 when `--count` was
  satisfied, 130 otherwise. Output order of matches across devices is not guaranteed.
- `--threads` note, the secret warning, `--out`, `--count` all shared and unchanged.

### Tests

- `CudaBackend`: enumeration lists ≥ 1 device with names and `sm_` details; `Open(count)`
  throws `ArgumentOutOfRangeException`; `CudaContext.TryEnumerate` agrees with
  `--list-devices`.
- `MultiDeviceSearch` (Gpu tests, skipping per backend via `SkipOrFail`):
  two engines on `cuda:0` with a frequent pattern and a limit of 5 → exactly 5 reported
  (thread-safe counter), every reported address matches the pattern set, both workers
  searched > 0 candidates, no failures; `cuda:0` + `opencl:0` together, same assertions;
  cancellation from the caller stops all workers within seconds; per-device counters sum
  to the totals.
- Fake backends (no hardware): a backend whose `Open` throws → `Failure` set, others
  unaffected; all selectors failing → `DevicesOpened == 0`; a fake device whose engine
  cannot be built is out of scope (the engine needs a real kernel).
- CLI: `--gpu-devices` parsing (implies `Gpu`; conflict with `--backend`/`--device`);
  `ResolveDevices` for `all`, `cuda`, `opencl`, `backend:index` lists, flat indices, and
  invalid entries; `SearchRunner`-level end-to-end on `all` (skips when no device).
- Harness: GPU mode adds `--gpu-devices all --starts ab --count 2` expecting exit 0,
  `GPU[cuda:0]` and `GPU[opencl:0]` lines and `on 2 device(s)`; CPU-only mode expects
  `on 1 device(s)` with `GPU[opencl:0]`, and `--gpu-devices cuda` → exit 2 with
  `no matching device`.

### Documentation

README: `--gpu-devices` in the flag table; a "Multiple devices" paragraph (selection forms,
what `all` means on a one-card machine, start-up cost with several OpenCL devices, output
order); the Linux section's smoke list. This design doc gains a "Measured" section:
aggregate rate for `cuda:0`+`opencl:0` on Windows and for RTX+pocl in the container.

## Correctness strategy

1. Every match printed still comes out of a `GpuSearchEngine`, i.e. through the CPU
   re-verification gate; `MultiDeviceSearch` never touches device hits.
2. The `--count` limit is enforced by the existing lock in `MatchSink.TryReport`; the test
   with two racing engines pins "exactly N".
3. Failure isolation is pinned with fake backends; real-device behaviour is pinned with the
   three concrete configurations available here.
4. The whole suite stays green on Windows (both backends) and in both harness modes.

## Milestones (one implementation plan)

| Task | Deliverable | Gate |
| --- | --- | --- |
| 1 | CUDA enumeration for every ordinal; `TryCreate(ordinal)`; tests | Windows suite green |
| 2 | `MultiDeviceSearch` with stats, failure isolation, cancellation; Gpu tests incl. fake backends | tests green on cuda×2 and cuda+opencl |
| 3 | `--gpu-devices`, `ResolveDevices`, `RunMultiGpu`, CLI tests, README | Windows smokes: `all`, `cuda`, `0,1`, conflict → exit 2 |
| 4 | Harness smokes in both modes; Linux runs; Measured section | both modes green |

## Risks

- **Two engines on one card contend** — expected; the aggregate rate on Windows may not
  exceed a single engine's. Recorded, not optimised.
- **Start-up with several OpenCL devices is serial** (SetupLock) — documented; the compile
  note is logged per selector.
- **A worker exception during `RunLaunch`** — caught per worker, reported as that device's
  failure; the others continue. A failure that is really a shared-driver wedge would stop
  everything with the same message on every device, which is the honest outcome.
- **Ctrl+C responsiveness** — a worker checks cancellation between launches (a launch is a
  few milliseconds on a GPU, up to ~20 s per 2^20 candidates on pocl); `Run` joins workers
  without a timeout because every launch terminates on its own.

## Assumptions made on the operator's behalf

1. `--gpu` stays single-device; multi-device is an explicit `--gpu-devices` opt-in.
2. The orchestrator lives in `XrplVanity.Gpu`; the sink stays in the CLI behind a callback.
3. One device failing is a warning; none opening is exit 2.
4. Match output order across devices is unspecified.
5. Plan order C → D → E complete after this; secp256k1 permanently excluded.
