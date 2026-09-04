#!/usr/bin/env bash
# Builds the Linux test image and runs the whole suite inside it.
#   bash linux/run-linux-tests.sh            # GPU mode: needs the NVIDIA container runtime; requires cuda AND opencl (pocl)
#   bash linux/run-linux-tests.sh --cpu-only # no GPU: requires opencl (pocl); CUDA rows skip
# Works from Git Bash on Windows (Docker Desktop) and from any Linux shell.
set -euo pipefail

MODE=gpu
if [ "${1:-}" = "--cpu-only" ]; then MODE=cpu; elif [ -n "${1:-}" ]; then echo "usage: $0 [--cpu-only]" >&2; exit 2; fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE=xrpl-vanity-linux-tests

if command -v cygpath >/dev/null 2>&1; then SRC="$(cygpath -w "$ROOT")"; else SRC="$ROOT"; fi

echo "== building image $IMAGE (context: $ROOT/linux)"
docker build -t "$IMAGE" -f "$ROOT/linux/Dockerfile" "$ROOT/linux"

GPU_ARGS=()
if [ "$MODE" = gpu ]; then GPU_ARGS=(--gpus all); fi

echo "== running mode=$MODE"
MSYS_NO_PATHCONV=1 docker run --rm "${GPU_ARGS[@]}" \
    -v "$SRC:/src:ro" \
    -v xrpl-vanity-nuget:/cache/nuget \
    -v xrpl-vanity-pocl:/cache/pocl \
    "$IMAGE" "$MODE"
