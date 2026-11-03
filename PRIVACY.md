# Privacy policy

**XRPL Vanity** — desktop application for Windows, published by StaticBit.

Last updated: 2026-09-16.

## Summary

The application collects nothing, sends nothing, and has no account system. It has no
network code at all: no telemetry, no analytics, no crash reporting, no update check, no
licence check. Everything it produces stays on the computer that produced it.

## What the application stores

Both files live in the application's own local data folder. The Settings page prints the
resolved path and opens the folder on request.

| File | Contents | Protection |
| --- | --- | --- |
| `settings.json` | Interface preferences: pattern and target drafts, thread count, GPU duty, match limit, window size, close behaviour, notification flag, clipboard and reveal timeouts, and the last measured search rate per device. | Plain JSON. It holds no keys and no personal data. |
| `results.dat` | The generated addresses and their seeds. | Encrypted with the Windows Data Protection API (DPAPI) under `CurrentUser` scope, so only the Windows account that wrote the file can decrypt it. |

A seed is the secret that controls an XRP Ledger account. `results.dat` is the only file
the application writes that contains one. No seed is ever written to a log, shown in a
notification, or placed in the tray menu.

If you export results to a TSV file, that file contains seeds in plain text. It is written
where you ask for it, and it is not encrypted — protecting it is up to you.

## Clipboard

Copying a seed places it on the Windows clipboard and clears it after a delay you
configure, and only if the clipboard still holds that same seed. Copying anything else in
the meantime leaves your newer content alone.

## Network

The application makes no network connections. The only traffic it can cause is a link you
click yourself in the About section or elsewhere in the interface, which opens in your
default browser; from that point the destination site's own privacy policy applies. The
address search itself never contacts the XRP Ledger or any server — it generates and tests
candidates entirely offline.

## Children

The application is not directed at children and collects no information from anyone.

## Removing your data

Uninstalling removes the application. To delete the stored results first, use **Open
folder** on the Settings page and delete `results.dat` and `settings.json`.

## Contact

Questions about this policy: https://github.com/StaticBit-io/xrpl-vanity-generator/issues
