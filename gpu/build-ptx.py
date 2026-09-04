#!/usr/bin/env python3
"""Compile cuda_shim.h + xrpl_vanity_body.h to PTX with NVRTC. No host C++ compiler needed.

NVRTC comes from the `nvidia-cuda-nvrtc` pip wheel (pip install --user nvidia-cuda-nvrtc);
set NVRTC_DIR to override the lookup. Target: compute_75 (Turing and newer; the driver
JIT-compiles the PTX for the actual GPU).
"""
import ctypes as C
import hashlib
import os
import site
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHIM = os.path.join(ROOT, "gpu", "kernels", "cuda_shim.h")
BODY = os.path.join(ROOT, "gpu", "kernels", "xrpl_vanity_body.h")
PTX = os.path.join(ROOT, "gpu", "kernels", "xrpl_vanity.ptx")
ARCH = b"compute_75"


def find_nvrtc_dir() -> str:
    env = os.environ.get("NVRTC_DIR")
    if env:
        return env
    candidates = [site.getusersitepackages()] + site.getsitepackages()
    for base in candidates:
        for cu in ("cu13", "cu12"):
            for sub in (("nvidia", cu, "bin", "x86_64"), ("nvidia", cu, "lib")):
                path = os.path.join(base, *sub)
                if os.path.isdir(path):
                    return path
    sys.exit("NVRTC not found: run `pip install --user nvidia-cuda-nvrtc` or set NVRTC_DIR")


def load_nvrtc(directory: str) -> C.CDLL:
    if os.name == "nt":
        if hasattr(os, "add_dll_directory"):
            os.add_dll_directory(directory)
        os.environ["PATH"] = directory + os.pathsep + os.environ.get("PATH", "")
        names = [f for f in os.listdir(directory) if f.startswith("nvrtc64_") and f.endswith(".dll") and "builtins" not in f]
    else:
        names = [f for f in os.listdir(directory) if f.startswith("libnvrtc.so") and "builtins" not in f]
    if not names:
        sys.exit(f"no NVRTC library in {directory}")
    return C.CDLL(os.path.join(directory, sorted(names)[-1]))


def main() -> None:
    nvrtc = load_nvrtc(find_nvrtc_dir())
    with open(SHIM, "rb") as f:
        shim = f.read()
    with open(BODY, "rb") as f:
        body = f.read()
    source = shim + b"\n" + body
    body_hash = hashlib.sha256(source.replace(b"\r\n", b"\n")).hexdigest()

    program = C.c_void_p()
    if nvrtc.nvrtcCreateProgram(C.byref(program), source, b"xrpl_vanity.cu", 0, None, None) != 0:
        sys.exit("nvrtcCreateProgram failed")

    options = [b"--gpu-architecture=" + ARCH]
    array = (C.c_char_p * len(options))(*options)
    rc = nvrtc.nvrtcCompileProgram(program, len(options), array)

    log_size = C.c_size_t()
    nvrtc.nvrtcGetProgramLogSize(program, C.byref(log_size))
    if log_size.value > 1:
        log = C.create_string_buffer(log_size.value)
        nvrtc.nvrtcGetProgramLog(program, log)
        sys.stderr.write(log.value.decode(errors="replace") + "\n")
    if rc != 0:
        sys.exit(f"NVRTC compile failed (rc={rc})")

    ptx_size = C.c_size_t()
    nvrtc.nvrtcGetPTXSize(program, C.byref(ptx_size))
    ptx = C.create_string_buffer(ptx_size.value)
    nvrtc.nvrtcGetPTX(program, ptx)
    with open(PTX, "wb") as f:
        f.write(b"// body-sha256: " + body_hash.encode("ascii") + b"\n")
        f.write(ptx.value)
    print(f"wrote {PTX} ({len(ptx.value)} bytes, {ARCH.decode()}, body-sha256 {body_hash[:12]}...)")


if __name__ == "__main__":
    main()
