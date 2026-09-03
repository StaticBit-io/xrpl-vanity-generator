// OpenCL C 1.2 shim for the single-source kernel body. The host concatenates this file and
// xrpl_vanity_body.h and compiles them at run time with -cl-std=CL1.2. Keep in lockstep with
// cuda_shim.h. No generic address space is assumed: GLOBAL marks the only pointers into
// device buffers that device functions accept.
typedef unsigned char u8;
typedef unsigned int u32;
typedef ulong u64;
typedef char i8;

#define KERNEL __kernel
#define DEVICE
#define DEVICE_INLINE static inline
#define GLOBAL __global
#define CONSTANT __constant
#define CONSTANT_TABLE __constant
#define GLOBAL_ID() ((int)get_global_id(0))
#define U64_C(x) x##ul
#define ATOMIC_INC(p) atomic_inc((p))
