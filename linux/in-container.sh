#!/usr/bin/env bash
# Runs inside the xrpl-vanity-linux-tests container. Usage: in-container.sh <gpu|cpu>
#   gpu: CUDA (passed-through GPU) and OpenCL (pocl) are both required.
#   cpu: only OpenCL (pocl) is required; CUDA rows skip.
set -euo pipefail

MODE="${1:-gpu}"
case "$MODE" in gpu|cpu) ;; *) echo "usage: $0 <gpu|cpu>" >&2; exit 2 ;; esac

step() { printf '\n== %s\n' "$*"; }
expect_contains() { # expect_contains <label> <needle> <haystack>
    if printf '%s' "$3" | grep -q -- "$2"; then printf 'ok   %s\n' "$1"; else printf 'FAIL %s: expected "%s"\n%s\n' "$1" "$2" "$3"; exit 1; fi
}
expect_exit() { # expect_exit <label> <expected> <actual>
    if [ "$3" -eq "$2" ]; then printf 'ok   %s (exit %s)\n' "$1" "$3"; else printf 'FAIL %s: expected exit %s, got %s\n' "$1" "$2" "$3"; exit 1; fi
}

step "copying sources from /src to /work (excluding bin, obj, .git, .superpowers, docs)"
mkdir -p /work
tar -C /src --exclude=bin --exclude=obj --exclude=.git --exclude=.superpowers --exclude=docs -cf - . | tar -C /work -xf -

step "environment"
clinfo -l || true
if command -v nvidia-smi >/dev/null 2>&1; then nvidia-smi -L || true; else echo "nvidia-smi: not present (no GPU passthrough)"; fi
dotnet --version

step "build"
dotnet build XrplVanity.sln -c Release

if [ "$MODE" = gpu ]; then export XRPL_VANITY_REQUIRE_BACKENDS=cuda,opencl; else export XRPL_VANITY_REQUIRE_BACKENDS=opencl; fi
step "tests (required backends: $XRPL_VANITY_REQUIRE_BACKENDS)"
# Captured via a redirect to a file rather than a pipe: piping the suite's stdout/stderr through
# `tee` (even to /dev/stderr) changes fd/buffering characteristics enough to trip a pocl race
# (pocl_release_dlhandle_cache assertion, observed reproducibly in --cpu-only mode) that a plain
# redirect does not.
SUITE_LOG="$(mktemp)"
dotnet run --project tests/XrplVanity.Tests -c Release --no-build > "$SUITE_LOG" 2>&1
cat "$SUITE_LOG"
SUITE="$(cat "$SUITE_LOG")"
rm -f "$SUITE_LOG"
if [ "$MODE" = gpu ]; then expect_contains "no skipped rows" "Skipped: 0," "$SUITE"; fi

CLI=(dotnet run --project src/XrplVanity.Cli -c Release --no-build --)

step "smoke: --list-devices"
LIST="$("${CLI[@]}" --list-devices)"; printf '%s\n' "$LIST"
expect_contains "list shows opencl:0" "opencl:0" "$LIST"
if [ "$MODE" = gpu ]; then expect_contains "list shows cuda:0" "cuda:0" "$LIST"; fi

step "smoke: --backend opencl"
set +e; OUT="$("${CLI[@]}" --backend opencl --starts ab --count 1 2>&1)"; RC=$?; set -e
expect_exit "opencl search" 0 "$RC"; expect_contains "ran on opencl" "GPU: opencl:" "$OUT"; expect_contains "no consistency errors" "consistency errors: 0" "$OUT"

step "smoke: --gpu (first available backend)"
set +e; OUT="$("${CLI[@]}" --gpu --starts ab --count 1 2>&1)"; RC=$?; set -e
expect_exit "gpu search" 0 "$RC"
if [ "$MODE" = gpu ]; then expect_contains "ran on cuda" "GPU: cuda:" "$OUT"; else expect_contains "fell to opencl (no CUDA)" "GPU: opencl:" "$OUT"; fi

if [ "$MODE" = cpu ]; then
    step "smoke: --backend cuda without a GPU must exit 2"
    set +e; OUT="$("${CLI[@]}" --backend cuda --starts ab --count 1 2>&1)"; RC=$?; set -e
    expect_exit "cuda unavailable" 2 "$RC"; expect_contains "reason printed" "no usable device" "$OUT"
fi

step "done: mode=$MODE, all expectations met"
