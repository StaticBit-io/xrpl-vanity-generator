// CUDA shim for the single-source kernel body. build-ptx.py concatenates this file and
// xrpl_vanity_body.h and hands the result to NVRTC. Keep in lockstep with opencl_shim.h.
typedef unsigned char u8;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed char i8;

#define KERNEL extern "C" __global__
#define DEVICE __device__
#define DEVICE_INLINE __device__ __forceinline__
#define GLOBAL
#define CONSTANT
#define CONSTANT_TABLE __device__ __constant__
#define GLOBAL_ID() (blockIdx.x * blockDim.x + threadIdx.x)
#define U64_C(x) x##ull
#define ATOMIC_INC(p) atomicAdd((p), 1u)
