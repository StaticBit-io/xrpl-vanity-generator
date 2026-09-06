# Benchmarks

Measured throughput of xrpl-vanity on one reference machine, with the commands that
produced each figure. Use this page to set expectations and to compare a change against
a recorded baseline. It is not a guarantee: throughput depends on the GPU, the driver, the
CPU that re-verifies hits, and how often the pattern matches.

All figures were measured between 2026-09-03 and 2026-09-04 on the commits noted in each
section. Rates are candidates (derived addresses) per second.

## Reference machine

| Component | Value |
| --- | --- |
| CPU | Intel Core i5-11400F, 6 cores / 12 threads |
| GPU | NVIDIA GeForce RTX 5070 Ti, 16 GB, compute capability 12.0 (`sm_120`) |
| NVIDIA driver | 610.74 (CUDA 13.3 runtime; OpenCL 3.0 with OpenCL C 1.2) |
| RAM | 64 GB |
| OS | Windows 11 Pro 26200 |
| .NET SDK | 10.0.30x |
| Linux harness | Docker Desktop (WSL2 backend, kernel 6.6), image `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04), 12 vCPU, pocl 5.0 (LLVM 16), CUDA through GPU passthrough |

The machine has one physical GPU. Everything labelled "multi-device" below runs that one
card through two APIs, or the card plus pocl (a CPU OpenCL implementation) inside the
Linux container. Scaling across two or more NVIDIA cards was not measured.

## How the figures were obtained

**GPU paths.** The CLI prints
`GPU: searched N candidates in T s (R/s); consistency errors: E` at the end of a run, and
one such line per device plus an aggregate under `--gpu-devices`. On every per-device line
`T` covers the search loop only: device open, CUDA context creation and OpenCL program
compilation are excluded. The multi-device aggregate line is the exception — its `T` is the
session's wall time, which is what the devices actually overlapped in, so it includes the
slowest device's start-up and reads slightly below the sum of the per-device rates. Every
device hit is re-derived by the CPU reference before it is printed, and that verification is
inside `T`, so the CLI rate falls when the pattern matches often (each hit costs one CPU
derivation on the launching thread). The figures below use
`--case-sensitive --starts abcd` (one match per ≈4.6 million candidates on average — `a` is
one of the 22 alphabet characters that lead the leading-character distribution below, so the
expectation is `23.34 × 58^3`, not `58^4`) and `--count 30`, so hits are rare and the rate
reflects the device.

**Kernel benchmark test.** `GpuSearchTests.Throughput_Meets_The_Floor_For_The_Device_Class`
runs four launches of 2^20 candidates after a warm-up with a pattern that never matches.
It measures the device pipeline alone (derivation, matching, hit-buffer drain with no
hits). It asserts a floor of 500,000/s for GPU devices and 10,000/s for CPU devices.

**Expected candidates per match.** Addresses use the 58-character XRPL base58 alphabet
`rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz`. A classic address is
base58check of a zero version byte, a 20-byte hash, and a 4-byte checksum; the zero byte
becomes the fixed leading `r`, and the remaining 192-bit number does not divide evenly
into base58 digits (`2^192 / 58^32 ≈ 23.34`), so the character right after `r` is not
uniform over the alphabet. A case-sensitive prefix of length *n* matches once per
`23.34 × 58^(n−1)` candidates when its first character is one of
`pshnaf39wBUDNEGHJKLM4P`, once per `69 × 58^(n−1)` when it is `Q`, and once per
`1,354 × 58^(n−1)` for any other first character; suffixes and substrings stay uniform at
`58^n`, since the checksum tail and interior positions are unaffected. Measured on this
machine with `--case-sensitive --starts X --threads 12`: `a` and `p` matched once per
~24 candidates, `Q` once per ~62, and `c` once per ~1,600 — consistent with the model.
Case-insensitive patterns match every case variant of each letter, except `o` and `i`
(no upper-case form in the alphabet) and `L` (no lower-case form).

**CPU path.** The CLI now counts candidates itself and prints a
`CPU: searched N candidates in T s (R/s)` line, so its rate is measured directly. An
earlier estimate (the superseded row in the CPU path table below) divided
`count × 58^length` by wall-clock time, which assumes a uniform `Starts` distribution;
the leading-character model above gives the correct expectation for a prefix. The
candidate count for a random search still follows a Poisson-like distribution, so a run
with `--count 60` has roughly ±13% statistical spread; wall time includes about one
second of process start-up. Two runs are reported.

**Earlier figures.** Where a figure was recorded in a design document during the work
(the "Measured" sections of the documents under `docs/`), it is quoted with its source.

## CPU path

Windows, 12 worker threads (the default), commit 8b66560.

| Implementation | Rate | Source |
| --- | --- | --- |
| `nhartner/xrp-vanity-address` (Java, GraalVM native image) | ~39,000/s | measured 2026-09-02, recorded in `2026-09-03-xrpl-vanity-design.md` |
| .NET prototype (throwaway spike) | ~138,000/s | same document |
| `xrpl-vanity --case-sensitive --starts abc --count 60` | superseded — the estimate assumed 58^3 per match; the measured rate is the `CPU:` row below | this page |
| `xrpl-vanity --case-sensitive --starts abc --count 60` (CPU: line) | 118,885/s and 111,576/s (measured from the `CPU: searched … (R/s)` line, Plan F) | this page (Plan F) |

The shipped CLI is faster than the spike because it uses span-based, allocation-free
derivation and per-thread derivers; the estimate's spread is statistical, not thermal.
The Plan F row is a direct measurement, not an estimate, now that the CPU path counts
candidates itself — it comes in well below the earlier wall-clock estimate above it.

## Single GPU device

Windows, commit 8b66560, 2026-09-04.

| Backend | Kernel benchmark test | CLI end-to-end, rare hits (`--case-sensitive --starts abcd --count 30`) | CLI end-to-end, frequent hits (`--starts abc`, case-insensitive; Plan C record) |
| --- | --- | --- | --- |
| `cuda` | 19,300,000/s | 19,323,026/s and 19,491,178/s (157,286,400 candidates in 8.1 s; 170,917,888 in 8.8 s) | 10,700,000/s |
| `opencl` (NVIDIA) | 21,000,000/s | 21,100,000/s (105,906,176 candidates in 5.0 s) | 12,100,000/s |

The `cuda` CLI figures were re-measured on 2026-09-06 at commit dd0366f, after the search
moved behind `SearchSession` and the per-device line went back to the device's own clock.
They bracket the 19,400,000/s recorded for the pre-session loop on commit 8b66560 (−0.4%
and +0.5%, average 19,407,102/s, +0.04%), so the session layer adds no measurable
overhead. The earlier Plan F figures of ≈18,400,000/s were an artefact of dividing by the
session clock, which charged CUDA context creation and PTX load to the search loop.

Reading the table: with rare hits the CLI reaches the kernel rate; with frequent hits
(case-insensitive `abc` matches once per ~24,000 candidates, about 400 hits per launch)
the CPU re-verification of hits roughly halves the end-to-end rate. Choose longer or
case-sensitive patterns when throughput matters; the gate is never skipped.

OpenCL is compiled at run time. On this card a warm compile (NVIDIA's on-disk compute
cache) takes about one second; a cold compile took 30–40 s (recorded in
`2026-09-03-xrpl-vanity-gpu-opencl-design.md`). The CUDA path loads precompiled PTX and
starts in under a second.

## Multiple devices

`--gpu-devices` runs one engine per device; `--count` is shared and exact.

| Configuration | Result | Source |
| --- | --- | --- |
| Windows, `--gpu-devices all --case-sensitive --starts abcd --count 30` (`cuda:0` + `opencl:0`, the same card through two APIs) | 19,326,747/s aggregate on the session clock (`cuda:0` 10,246,649/s over 8.8 s + `opencl:0` 9,594,758/s over 8.7 s; 174,063,616 candidates in 9.0 s) — no gain over one engine, because both engines share one GPU | this page (commit dd0366f, 2026-09-06) |
| Linux container, `--gpu-devices all --starts ab` (RTX through CUDA + pocl on 12 vCPU) | pocl's first launch satisfied `--count` before the CUDA worker finished opening; CUDA reported 0 candidates. Observation, not a defect: pocl saturates all vCPUs during its 25 s launch and starves the host-side CUDA driver calls | `2026-09-04-xrpl-vanity-multigpu-design.md` |
| Two engines on `cuda:0` (test `Two_Engines_On_The_Same_Cuda_Device_Stop_Exactly_At_The_Limit`) | limit reached exactly; both engines searched | test suite |

## Linux harness and pocl

Measured inside the Docker container (commit 045da70 unless noted).

| Path | Rate / time | Source |
| --- | --- | --- |
| pocl CPU device, kernel benchmark test | 56,114/s | `2026-09-03-xrpl-vanity-linux-design.md` |
| pocl CPU device, CLI `--backend opencl` | 58,115/s | same document |
| CUDA through passthrough, kernel benchmark test | 19,079,033/s | same document |
| CUDA through passthrough, CLI single launch (`--count 2`) | 2,555,684/s (one launch of 2^20 candidates; start-up dominated) | `2026-09-04-xrpl-vanity-multigpu-design.md` |
| Test suite wall time, GPU mode (`cuda,opencl` required, 0 skipped) | 959 s (254 tests, Plan F final, commit 8917a9a) | this page (Plan F) |
| Test suite wall time, CPU-only mode (pocl only) | 925.205 s (240 tests, 64 skipped — CUDA-gated, Plan F) — measured before the final fix wave | this page (Plan F) |
| Test suite wall time, Windows | 45.817 s (240 tests, commit 3e53441) | this page (Plan F) |

The Linux suite is slow because OpenCL device setup (enumeration, context creation,
program build, kernel creation and teardown) is serialised process-wide; pocl crashed
under concurrent setup from parallel test classes. The serialisation costs nothing at
run time: the CLI opens one device per run, or one per selected device. The GPU- and
CPU-only mode times grew further under Plan F because the CPU-saturating and
timing-sensitive test classes (`DutyCycleTests`, `MultiDeviceSearchTests`,
`CpuSearchWorkersTests`, `SearchSessionTests`, `CliProgramTests`) now share one xUnit
collection with `DisableParallelization = true`, and a single pocl 2^20 launch takes
about 18–25 s, so those classes run one at a time instead of in parallel.

## Duty cycle

`--gpu-duty N` pauses between launches for `launch time × (100 − N) / N`. Utilisation
was sampled with `nvidia-smi --query-gpu=utilization.gpu` once per second over eight
seconds while a never-matching search ran (`--case-sensitive --starts zzzzzzzz`), CUDA
backend, Windows, 2026-09-05.

| `--gpu-duty` | Sampled GPU utilisation (8 samples) | Average |
| --- | --- | --- |
| 100 (default) | 99 99 99 98 98 98 97 98 | 98% |
| 50 | 53 53 53 53 54 46 45 53 | 51% |
| 25 | 2 37 10 21 5 56 3 49 | 23% (a one-second sample window is shorter than the pauses, hence the spread) |

Throughput follows the duty: `--gpu --gpu-duty 80 --case-sensitive --starts abcd --count 5`
reported 13,465,682/s (24,117,248 candidates in 1.8 s) against 19,400,000/s at full
duty, and `--gpu-devices all --gpu-duty 50` reported 12,648,676/s aggregate. The rate
line counts the pauses as search time, so it reflects what the user actually gets.

## Not measured

- Scaling across two or more NVIDIA cards (one GPU on the reference machine).
- AMD and Intel GPUs (no hardware; the OpenCL path is validated on NVIDIA OpenCL and on
  pocl only).
- pocl's cold program compile (every recorded run used the warm `xrpl-vanity-pocl` cache
  volume).
- Sustained throughput over minutes or hours (every run above lasts seconds; thermal
  throttling was observed once during earlier CPU spikes and is why A/B comparisons were
  interleaved).

## Reproduce

Run from the repository root. Build once, then use `--no-build` to keep start-up out of
the way.

```bash
dotnet build XrplVanity.sln -c Release
```

Single device, rare hits (the numbers in "Single GPU device"):

```bash
dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --backend cuda --case-sensitive --starts abcd --count 30
dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --backend opencl --case-sensitive --starts abcd --count 30
```

Both devices of this machine at once:

```bash
dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --gpu-devices all --case-sensitive --starts abcd --count 30
```

Utilisation under a duty cycle (run the search in one terminal, sample in another):

```bash
dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --gpu --gpu-duty 50 --case-sensitive --starts zzzzzzzz
```

```bash
nvidia-smi --query-gpu=utilization.gpu --format=csv,noheader -l 1
```

Kernel benchmark test on every available backend (prints one
`GPU search throughput: … on <backend>:…` line per backend):

```bash
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"
```

CPU path (the CLI prints its own `CPU: searched … (R/s)` line; no estimate needed):

```bash
time dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --case-sensitive --starts abc --count 60
```

Linux container, both harness modes (see the README's Linux section for requirements):

```bash
bash linux/run-linux-tests.sh
bash linux/run-linux-tests.sh --cpu-only
```

Every printed seed in these runs is a real secret for the address next to it. Discard
the output of benchmark runs.
