# Store listing copy

Ready-to-paste text for the Partner Center listing. Keep it in sync with the application
when features change.

**Category:** Developer tools
**Suggested search terms:** XRPL, XRP Ledger, vanity address, address generator, Ed25519,
GPU, offline

## Product name

```
XRPL Vanity
```

## Short description

```
Generate an XRP Ledger address that contains the word you choose. Runs entirely offline on your CPU and GPU.
```

## Description

```
XRPL Vanity searches for an XRP Ledger account whose address contains a word you pick — a
vanity address. It generates candidate key pairs, checks each address against your
patterns, and stops when it finds a match.

Everything happens on your machine. The application has no network code at all: no account,
no server, no telemetry, no update check. Your seeds are generated locally and never leave
the computer.

PATTERNS

Anchor a pattern to the start of the address, to the end, or anywhere inside it, and choose
whether case matters. A single run can combine several patterns, and a paired pattern
requires a prefix and a suffix on the same address.

Before a run starts, the application estimates how many candidates a match will take and
how long that is likely to be on the hardware you selected, so you know whether a pattern
is worth waiting for.

SPEED

Search on the CPU with as many threads as you want, on every CUDA and OpenCL device in the
machine, or on all of them at once. A duty control from 25 to 100 percent limits how hard
the GPU is driven and takes effect immediately, so you can keep using the computer while a
long search runs.

RESULTS

Found addresses are stored encrypted with the Windows Data Protection API, readable only by
the Windows account that created them. Seeds stay masked until you ask to see one, hide
themselves again after a timeout, and a seed copied to the clipboard is cleared after a
delay you set. A result can be shown as a QR code or exported to a file.

A tray icon keeps a long search out of the way, and a notification tells you when a match
arrives or a run finishes.

This application generates Ed25519 keys. It does not support secp256k1 addresses.
```

## Key features

```
Vanity XRP Ledger addresses matching a prefix, a suffix, or a substring
Fully offline: no network connection, no account, no telemetry
CPU search with a configurable thread count
GPU search on CUDA and OpenCL devices, several at once
Duty control to cap GPU load while you keep working
An estimate of candidates and time before a run starts
Results encrypted at rest with DPAPI
Masked seeds, clipboard auto-clear, and QR export
```

## Notes for certification

Paste into **Notes for certification** on the submission page.

```
This application is not a cryptocurrency miner and performs no mining of any kind.

It generates random Ed25519 key pairs locally and checks whether the resulting XRP Ledger
address contains a pattern the user typed — a "vanity address" generator. It performs no
proof-of-work, earns no currency, joins no network, and makes no network connection
whatsoever. The GPU is used only to test candidate addresses faster, and the user controls
how hard it is driven. A search ends as soon as a matching address is found.

The application is open source. Source code:
https://github.com/StaticBit-io/xrpl-vanity-generator

Generated seeds are stored locally, encrypted with the Windows Data Protection API under
the current user, and are never transmitted. Privacy policy:
https://xrpl-vanity.staticbit.io/privacy/

The package declares runFullTrust because it is a packaged desktop application that loads
CUDA and OpenCL drivers and shows a tray icon.
```
