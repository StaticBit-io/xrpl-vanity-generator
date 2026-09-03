// XRPL vanity — CUDA kernels. Device-only source compiled to PTX by gpu/build-ptx.py.
// Every section below is verified by tests/XrplVanity.Tests/Gpu before the next is built on it.

typedef unsigned char u8;
typedef unsigned int u32;
typedef unsigned long long u64;

// ---------------------------------------------------------------------------
// M0: infrastructure probe
// ---------------------------------------------------------------------------
extern "C" __global__ void k_add_arrays(const u32* a, const u32* b, u32* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    out[i] = a[i] + b[i];
}
