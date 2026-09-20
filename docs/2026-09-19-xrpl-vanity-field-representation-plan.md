# Field arithmetic: relaxed canonicalisation, then a redundant representation — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the per-operation canonicalisation that costs 26% of the GPU kernel's run time, then decide by measurement whether to replace the 8 × 32-bit saturated limb representation with a ref10-style redundant one.

**Architecture:** Three stages of increasing blast radius, each measured before the next is started. Stage A (already applied) stops re-canonicalising values loaded from the base table. Stage B keeps the 8 × 32 representation but lets field elements stay non-canonical between operations, reducing only where a canonical value is actually observed. Stage C replaces the representation with ten limbs of alternating 26 and 25 bits, signed, as ref10 uses, which removes canonicalisation by construction and breaks the serial carry chain inside the multiply. **Stage C only starts if the gate after Stage B says there is still something to win.**

**Tech Stack:** Single-source CUDA/OpenCL kernel (`gpu/kernels/xrpl_vanity_body.h`), compiled to PTX by NVRTC through `gpu/build-ptx.py` and at run time by the NVIDIA OpenCL compiler. .NET 10 host, xUnit v3 with the built-in `Assert`.

**Spec:** none — this plan is its own spec. The measurements it argues from are recorded in `docs/benchmarks.md` and in the Background section below.

## Global Constraints

- **Tests run with `dotnet run`, never `dotnet test`** — `dotnet test` does not work with xunit.v3 on net10. Whole suite: `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`. One class: `dotnet run --project tests/XrplVanity.Tests -c Release --no-build -- -class "*GpuFieldTests"`.
- **Regenerate the PTX after every kernel edit:** `python gpu/build-ptx.py`, then commit `gpu/kernels/xrpl_vanity.ptx` with the body. `KernelSourceTests` fails the build otherwise.
- **The build must stay warning-free.** `dotnet build XrplVanity.sln -c Release` prints only `Сборка успешно завершена.`
- **`cuda_shim.h` and `opencl_shim.h` stay in lockstep.** Any type or macro added to one is added to the other in the same commit.
- **Measure by interleaving, never across sessions.** Two runs of one unchanged configuration minutes apart read 25,680,843/s and 23,461,480/s — a 9% spread. Every figure in this plan comes from alternating the two variants inside one session, rebuilding between them, and comparing means.
- **Check `nvidia-smi` before measuring.** Utilisation above a few percent means something else holds the card and the result is roughly halved with no sign of it in the output.
- **The measurement harness is the throughput test, not the CLI.** It uses a never-matching pattern, so no CPU verification pollutes the number:
  ```bash
  dotnet run --project tests/XrplVanity.Tests -c Release --no-build -- -method "*Throughput_Meets_The_Floor*" -xml /tmp/probe.xml
  grep -oE "GPU search throughput: [0-9,]+ candidates/s on [a-z]+:" /tmp/probe.xml
  ```
- Explicit types instead of `var` in C#. Comments in English, and only where the code cannot say it itself. No AI attribution in commits.

## Background: what the measurements say

Deriving one address costs 747 field multiplications after the September 19 work: 480 in the windowed scalar multiplication, 265 in the inversion, 2 in `pt_compress`.

The kernel's field layer holds an invariant stated at the top of the M1 section: *every function that produces a field element returns it canonical (fully reduced, < p)*. `fe_reduce_wide` enforces it with `fe_canon`, which is two `fe_cond_sub_p` calls; `fe_add` and `fe_sub` each do one. Per candidate that is roughly:

| Source | `fe_cond_sub_p` calls |
| --- | --- |
| `fe_mul` / `fe_sqr` (551 × 2) | 1102 |
| `fe_add` / `fe_sub` (~276 × 1) | 276 |
| `fe_from_bytes_g` on base-table loads (192 × 2) | 384 |
| **Total** | **~1762** |

Replacing `fe_canon` with a no-op — incorrect, timing only — measured:

| Variant | cuda | opencl |
| --- | --- | --- |
| Correct | 27,585,277/s | 27,011,710/s |
| `fe_canon` no-op | 34,868,483/s | 33,187,739/s |
| | **+26.4%** | **+22.9%** |

So canonicalisation, not the serial carry chain inside `fe_mul`, is the largest single identified cost. This matters because it can be removed without changing the representation, which is what Stage B does.

Stage A has already been applied and measured (+5.4% on cuda): the base table only ever holds canonical values, because `fe_to_bytes` is a pure serialiser fed by operations that all return canonical, so the `fe_canon` inside `fe_from_bytes_g` could never do anything.

---

### Task 1: Land Stage A

Stage A is already in the working tree and passes the full suite. This task only commits it, so the later measurements have a named baseline.

**Files:**
- Modify: `gpu/kernels/xrpl_vanity_body.h` (the `fe_from_bytes_g` body)
- Modify: `gpu/kernels/xrpl_vanity.ptx` (regenerated)

**Interfaces:**
- Consumes: nothing.
- Produces: `fe_from_bytes_g` no longer reduces. Its precondition — the buffer holds canonical values — is documented at the call site.

- [ ] **Step 1: Confirm the change is the only one in the tree**

```bash
git status --short
```

Expected: exactly `gpu/kernels/xrpl_vanity_body.h` and `gpu/kernels/xrpl_vanity.ptx` modified.

- [ ] **Step 2: Run the full suite**

```bash
dotnet build XrplVanity.sln -c Release && dotnet run --project tests/XrplVanity.Tests -c Release --no-build
```

Expected: `Total: 305, Errors: 0, Failed: 0`.

- [ ] **Step 3: Commit**

```bash
git add gpu/kernels/xrpl_vanity_body.h gpu/kernels/xrpl_vanity.ptx
git commit -m "perf(gpu): the base table is already canonical, so loading it does not reduce"
```

---

### Task 2: Stage B — let field elements stay non-canonical

The invariant changes from *canonical* to *bounded*: every field element is an 8-limb value strictly below 2^256, congruent to the intended value mod p. Reduction happens only where a canonical value is observed, which is exactly two places: `fe_to_bytes` and the low-bit test in `address_from_point`.

`fe_mul` and `fe_sqr` already accept any 8-limb input — their accumulator bound is `(2^32-1)^2 + 2*(2^32-1) = 2^64-1`, which holds for arbitrary limbs, not just canonical ones. Only `fe_add` and `fe_sub` need work, because both currently assume inputs below p so that no carry escapes the top limb.

**Files:**
- Modify: `gpu/kernels/xrpl_vanity_body.h` — `fe_add`, `fe_sub`, `fe_reduce_wide`, `fe_to_bytes`, `address_from_point`, `k_fe_op`, and the M1 header comment
- Modify: `gpu/kernels/xrpl_vanity.ptx` (regenerated)
- Test: `tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs` (no change expected — see Step 1)

**Interfaces:**
- Consumes: Task 1's tree.
- Produces:
  - `DEVICE_INLINE void fe_add(Fe* r, const Fe* a, const Fe* b)` — accepts any inputs below 2^256, folds the carry out of limb 7 back with ×38, result below 2^256.
  - `DEVICE_INLINE void fe_sub(Fe* r, const Fe* a, const Fe* b)` — same contract, folds the borrow with ×38.
  - `DEVICE_INLINE void fe_canon(Fe* a)` — unchanged, but now called only from `fe_to_bytes`.
  - `fe_reduce_wide` no longer calls `fe_canon`.

- [ ] **Step 1: Confirm the existing tests already cover this**

`GpuFieldTests` drives `k_fe_op`, which writes raw limbs, and compares against `BigInteger` with `% P`. If `k_fe_op` keeps writing raw limbs, a non-canonical result fails those assertions even though the value is congruent — so `k_fe_op` must reduce before writing. That is the correct behaviour anyway: the test asks for a value, not a representation.

Read the test to confirm it compares values:

```bash
sed -n '/AssertEach/,+3p' tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs | head
```

Expected: assertions of the form `Map(a, b, (x, y) => (x * y) % P)` — value comparisons. No test asserts a particular limb pattern.

- [ ] **Step 2: Make `k_fe_op` reduce before writing, and run the suite to confirm it still passes**

In `k_fe_op`, immediately before the output loop, add:

```c
    fe_canon(&r);   // the test compares values; the representation is free between operations
```

Run:

```bash
python gpu/build-ptx.py && dotnet build XrplVanity.sln -c Release && dotnet run --project tests/XrplVanity.Tests -c Release --no-build -- -class "*GpuFieldTests"
```

Expected: PASS. This is a no-op today (everything is already canonical) and is what keeps the tests meaningful after the next step.

- [ ] **Step 3: Write the failing test — a multiply whose inputs are deliberately non-canonical**

Add to `tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs`:

```csharp
    /// <summary>
    /// Field elements are only required to be congruent and below 2^256 between operations,
    /// not fully reduced. Every operation must therefore accept an unreduced input and still
    /// produce the right value. These inputs are all >= p, which the canonical invariant
    /// could never have produced.
    /// </summary>
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Operations_Accept_Unreduced_Inputs(string backend)
    {
        BigInteger[] a = { P, P + 1, P + 37, 2 * P, 2 * P + 37, BigInteger.Pow(2, 256) - 1, BigInteger.Pow(2, 255) };
        BigInteger[] b = { P + 5, 2 * P, P, BigInteger.Pow(2, 256) - 2, P + 1, P + 19, 2 * P + 1 };

        AssertEach(RunOp(backend, OpAdd, a, b), Map(a, b, (x, y) => (x + y) % P));
        AssertEach(RunOp(backend, OpSub, a, b), Map(a, b, (x, y) => ((x - y) % P + P) % P));
        AssertEach(RunOp(backend, OpMul, a, b), Map(a, b, (x, y) => (x * y) % P));
        AssertEach(RunOp(backend, OpSqr, a, b), Map(a, b, (x, _) => (x * x) % P));
    }
```

- [ ] **Step 4: Run it and watch it fail**

```bash
dotnet run --project tests/XrplVanity.Tests -c Release --no-build -- -method "*Operations_Accept_Unreduced_Inputs*"
```

Expected: FAIL on `OpAdd` — `fe_add` currently drops the carry out of limb 7, so `(2^256-1) + (2^256-2)` loses 2^256 entirely instead of folding it to 38.

- [ ] **Step 5: Model the new `fe_add` and `fe_sub` in Python before touching the kernel**

Carry propagation is where this silently goes wrong, and a Python model catches it in seconds. Save as a scratch file and run it:

```python
M = 0xffffffff
p = 2**255 - 19

def limbs(x): return [(x >> (32 * i)) & M for i in range(8)]
def unlimb(v): return sum(x << (32 * i) for i, x in enumerate(v))

def fe_add(a, b):
    r, c = [0] * 8, 0
    for i in range(8):
        c += a[i] + b[i]
        r[i] = c & M
        c >>= 32
    # 2^256 == 38 (mod p): fold the carry back in, then once more for its own carry.
    add = 38 * c
    for i in range(8):
        if add == 0: break
        acc = r[i] + add
        r[i] = acc & M
        add = acc >> 32
    assert add == 0
    return unlimb(r)

def fe_sub(a, b):
    r, borrow = [0] * 8, 0
    for i in range(8):
        d = a[i] - b[i] - borrow
        r[i] = d & M
        borrow = 1 if d < 0 else 0
    # a borrow means the result is 2^256 short; subtracting 38 restores it mod p.
    sub = 38 * borrow
    for i in range(8):
        if sub == 0: break
        d = r[i] - sub
        r[i] = d & M
        sub = 1 if d < 0 else 0
    return unlimb(r)

import random
random.seed(3)
cases = [0, 1, p - 1, p, p + 1, 2 * p, 2**256 - 1, 2**255] + [random.randrange(0, 2**256) for _ in range(5000)]
bad = 0
for x in cases:
    for y in random.sample(cases, 12):
        if fe_add(limbs(x), limbs(y)) % p != (x + y) % p: bad += 1; print("ADD", hex(x), hex(y))
        if fe_sub(limbs(x), limbs(y)) % p != (x - y) % p: bad += 1; print("SUB", hex(x), hex(y))
print("mismatches:", bad)
```

Expected: `mismatches: 0`. If not, fix the model before writing any C.

- [ ] **Step 6: Implement it in the kernel**

Replace `fe_add` and `fe_sub` in `gpu/kernels/xrpl_vanity_body.h`:

```c
// r = a + b (mod p). Inputs and result are any 8-limb value below 2^256, not necessarily
// reduced: 2^256 == 38 (mod p), so a carry out of the top limb folds back in rather than
// being lost. Reduction happens only in fe_to_bytes.
DEVICE_INLINE void fe_add(Fe* r, const Fe* a, const Fe* b)
{
    u64 c = 0;
    for (int i = 0; i < 8; i++) {
        c += (u64)a->v[i] + (u64)b->v[i];
        r->v[i] = (u32)c;
        c >>= 32;
    }
    u64 add = U64_C(38) * c;
    for (int i = 0; i < 8 && add; i++) {
        u64 acc = (u64)r->v[i] + add;
        r->v[i] = (u32)acc;
        add = acc >> 32;
    }
}

// r = a - b (mod p). A borrow out of the top limb means the result came up 2^256 short, so
// subtracting 38 restores it. Same bound contract as fe_add.
DEVICE_INLINE void fe_sub(Fe* r, const Fe* a, const Fe* b)
{
    u64 borrow = 0;
    for (int i = 0; i < 8; i++) {
        u64 d = (u64)a->v[i] - (u64)b->v[i] - borrow;
        r->v[i] = (u32)d;
        borrow = (d >> 63) & U64_C(1);
    }
    u64 sub = U64_C(38) * borrow;
    for (int i = 0; i < 8 && sub; i++) {
        u64 d = (u64)r->v[i] - sub;
        r->v[i] = (u32)d;
        sub = (d >> 63) & U64_C(1);
    }
}
```

Remove the `fe_canon(&o);` line from `fe_reduce_wide`, and add the reduction to `fe_to_bytes`:

```c
// The only place a canonical value is observed. Everything upstream is free to carry an
// unreduced representative.
DEVICE_INLINE void fe_to_bytes(u8* out32, const Fe* a)
{
    Fe t;
    fe_copy(&t, a);
    fe_canon(&t);
    for (int i = 0; i < 8; i++) {
        out32[4 * i]     = (u8)(t.v[i]);
        out32[4 * i + 1] = (u8)(t.v[i] >> 8);
        out32[4 * i + 2] = (u8)(t.v[i] >> 16);
        out32[4 * i + 3] = (u8)(t.v[i] >> 24);
    }
}
```

Update the M1 header comment from "returns it CANONICAL (fully reduced, < p)" to state the new contract: congruent, below 2^256, reduced only by `fe_to_bytes`.

- [ ] **Step 7: Fix both places that read a limb directly**

Two functions test the sign of x with `x.v[0] & 1u`, which is only correct for a canonical
value: `address_from_point` (the hot path) and `pt_compress` (reached by the `k_scalarmult_base`
and `k_scalarmult_base_windowed` test kernels, so `GpuPointTests` and `GpuWindowedTests` fail if
it is missed). Both get the same treatment — in `pt_compress` the bytes are already being
written, so reduce there and read the low byte back:

```c
    u8 xb[32];
    fe_to_bytes(xb, &x);
    pub[32] |= (u8)((xb[0] & 1u) << 7);       // RFC 8032 s5.1.2: sign of x in the top bit
```

Confirm nothing else reads the representation. Everything outside the M1 field section and
the two sign tests must be clean — this was verified before the plan was written, and the
point arithmetic (`pt_add`, `pt_dbl`, `pt_madd`, `scalarmult_base_windowed`, `precomp_*`) is
written purely against the `fe_*` interface:

```bash
grep -n "\.v\[\|->v\[" gpu/kernels/xrpl_vanity_body.h | grep -vE "fe_zero|fe_one|fe_copy|fe_cond_sub_p|fe_add|fe_sub|fe_mul|fe_sqr|fe_reduce_wide|fe_from_bytes|fe_to_bytes|fe_load_const"
```

Expected after the fix: only the `k_fe_op` input and output loops. That same command is what
makes Stage C tractable, so keep it passing.

- [ ] **Step 8: Run the field tests, then the full suite**

```bash
python gpu/build-ptx.py && dotnet build XrplVanity.sln -c Release
dotnet run --project tests/XrplVanity.Tests -c Release --no-build -- -class "*GpuFieldTests"
dotnet run --project tests/XrplVanity.Tests -c Release --no-build
```

Expected: `Operations_Accept_Unreduced_Inputs` passes, then `Total: 306, Errors: 0, Failed: 0`. `GpuPipelineTests.Derives_The_Known_Vector` passing is the real gate: it checks the full derivation against a known address.

- [ ] **Step 9: Measure, interleaved**

Stash the two bodies and alternate three times, as described in Global Constraints. Record cuda and opencl means.

- [ ] **Step 10: Commit**

```bash
git add gpu/kernels/xrpl_vanity_body.h gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs
git commit -m "perf(gpu): field elements stay unreduced until someone looks at them"
```

---

### Gate: decide whether Stage C is worth starting

> **Answered 2026-09-20: Stage C is not started.** Stage B landed at 33,840,087/s on cuda
> against the 34,868,483/s the no-op probe had promised — 87% of the ceiling — and re-running
> that probe against the Stage B kernel now lands inside the noise (33.4M/s against 33.7M/s on
> cuda; on opencl the probe measured slower than the real thing, which it cannot be). The
> reduction cost is spent, so a redundant representation would earn only whatever the multiply's
> structure is worth, which is unknown and may be negative. The reasoning and the numbers are in
> `docs/benchmarks.md` under "Where the next gain would have to come from". Task 3 below stays as
> the groundwork should anyone pick it up.

Stage B removes the canonicalisation the probe priced at 26% of run time. Stage C's remaining value is whatever the *structure* of the multiply is worth — 100 independent accumulations against 64 with a serial carry chain of length 64 — and that is unknown.

- [ ] **Step 1: Re-run the no-op probe against the Stage B tree**

Apply the same incorrect `fe_canon` no-op probe to the Stage B kernel and measure. If Stage B captured the canonicalisation cost, the probe should now show almost no difference.

- [ ] **Step 2: Decide**

- If Stage B landed within a few percent of the earlier no-op probe figure (about 34.9M/s on cuda), canonicalisation is fully recovered and the only thing left is the multiply structure. **Stop and report the number before starting Stage C.** Stage C is roughly 500 lines across the whole field layer, changes the base-table format and the host's `TableBytes`, and has an unknown sign on a GPU, where thread-level parallelism already hides the latency that the redundant representation's ILP would relieve.
- If Stage B fell well short, find out why before writing Stage C, because Stage C inherits whatever the cause is.

---

### Task 3: Stage C — ref10 representation (only after the gate)

**The gate answered no on 2026-09-20.** This task is kept as groundwork, not as work to do:
the re-encoded constants, the blast radius, and the one behavioural break are recorded here so
that picking it up later does not start from nothing. Do not build it without a fresh reason
and a fresh measurement.

Representation: ten signed 32-bit limbs `h[0..9]`, value `Σ h[i]·2^off[i]` with
`off = [0, 26, 51, 77, 102, 128, 153, 179, 204, 230]`; even indices carry 26 bits, odd
indices 25.

**Files:**
- Modify: `gpu/kernels/cuda_shim.h` and `gpu/kernels/opencl_shim.h` — add `i32` and `i64`
- Modify: `gpu/kernels/xrpl_vanity_body.h` — the whole M1 field section, the M3 constants, `precomp_from_pt` / `precomp_load`, `k_fe_op`
- Modify: `src/XrplVanity.Gpu/GpuSearchEngine.cs` — `TableBytes`
- Modify: `tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs` — the `Canon` op's contract
- Modify: `gpu/kernels/xrpl_vanity.ptx` (regenerated)

**Interfaces:**
- Consumes: Task 2's tree.
- Produces: `Fe` becomes `typedef struct { i32 v[10]; } Fe;`. Every `fe_*` signature is unchanged, so `pt_add`, `pt_dbl`, `pt_madd`, `scalarmult_base_windowed` and `fe_inv` need no edits at all — they are written against the `fe_*` interface only. Verify that claim with `grep -n "\.v\[" gpu/kernels/xrpl_vanity_body.h` before starting; anything outside the M1 section is a leak that must be fixed first.

The constants, re-encoded (verified to round-trip exactly, maximum limb 54,557,047 < 2^26):

```c
CONSTANT_TABLE i32 D2_LIMBS[10] = { 45281625, 27714825, 36363642, 13898781, 229458, 15978800, 54557047, 27058993, 29715967, 9444199 };
CONSTANT_TABLE i32 BX_LIMBS[10] = { 52811034, 25909283, 16144682, 17082669, 27570973, 30858332, 40966398, 8378388, 20764389, 8758491 };
CONSTANT_TABLE i32 BY_LIMBS[10] = { 40265304, 26843545, 13421772, 20132659, 26843545, 6710886, 53687091, 13421772, 40265318, 26843545 };
CONSTANT_TABLE i32 BT_LIMBS[10] = { 28827043, 27438313, 39759291, 244362, 8635006, 11264893, 19351346, 13413597, 16611511, 27139452 };
```

Two contract changes to record in the plan's commit message and in `docs/benchmarks.md`:

1. **`fe_from_bytes` stops folding bit 255.** ref10's loader masks it, so `2^255` loads as `0` rather than `19`, and `2^256-1` as `18` rather than `37`. The hot path never sees such an input — it only decodes RFC 8032 encodings — but `GpuFieldTests.Canon_Reduces_Any_256_Bit_Value` does. Keep that test honest by giving the test op a wide loader that folds bit 255 with ×19 explicitly, rather than weakening the assertion.
2. **The base table entry grows from 96 to 120 bytes** (three elements of ten `i32`), so `GpuSearchEngine.TableBytes` becomes `256 * 120`. There is no host-side test pinning this constant to the kernel; add one in the shape of `KernelSourceTests.Kernel_Batch_Size_Matches_The_Host_Launch_Divisor`, because a mismatch corrupts the table silently rather than failing.

Task 3 is deliberately left at this level of detail. Expanding it into bite-sized TDD steps is worth doing only once the gate says it should be built, and the gate may well say it should not.

---

## Self-review

**Coverage.** Stage A is Task 1; Stage B is Task 2; the measurement gate is its own section; Stage C is Task 3, explicitly conditional. The 26% probe figure that motivates all of it is recorded in Background with the numbers behind it.

**Placeholders.** Task 3 is intentionally a specification rather than a step list, and says so with its reason; everything in Tasks 1 and 2 carries the exact code, the exact commands and the expected output. The Python model in Task 2 Step 5 is complete and runnable.

**Type consistency.** `fe_add`, `fe_sub`, `fe_canon`, `fe_to_bytes`, `fe_reduce_wide` and `fe_from_bytes_g` keep their existing signatures across every task; only their contracts change, and each change is stated where it happens. `Fe` changes shape only in Task 3, and the claim that no caller outside the M1 section touches `.v[]` is given a command to verify rather than asserted.
