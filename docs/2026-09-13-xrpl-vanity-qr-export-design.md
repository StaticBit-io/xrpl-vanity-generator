# QR export to the StaticBit wallet

Design for one addition to the desktop application: a found wallet — or a selection of them — can
be shown as a QR code that the StaticBit mobile wallet imports by scanning, with links to the
wallet on the App Store and Google Play under the code.

## Why

A found result leaves the application as a line of `address<TAB>seed` in a TSV file. Moving one
onto a phone therefore means typing a 31-character seed by hand, or moving the file — and the
file is the thing that must not travel. The StaticBit wallet already restores wallets by scanning
a QR code, so producing exactly the code it expects turns a found vanity address into a wallet on
the phone in one scan, and the seed never touches a file or the clipboard.

## What the code carries

The dialog offers two payloads and the user picks between them.

### The StaticBit backup container

Byte-for-byte the container the wallet's own backup produces, so its scanner recognises it. The
layout, all multi-byte integers big-endian:

```
"SBW1" (4) | version (1) | kdfId (1) | iterations (4) | salt (16) | nonce (12) | ciphertext (N) | tag (16)
```

`iterations`, `salt`, `nonce` and `tag` exist only when `kdfId != 0`. Without a password the whole
header is the six bytes `"SBW1" | 01 | 00` and the compressed payload follows it directly.

The plaintext is JSON, camelCase, no indentation, null fields omitted. One entry per wallet:

```json
{"wallets":[{"keyValue":"sEd…","classicAddress":"r…","keyType":1,"algorithm":"ed25519","description":"static..bit","folder":"Vanity"}]}
```

`keyType` is a number — `1` is the wallet's `AccountKeyType.Seed`; `0` means a watch-only account
and would make the wallet refuse to derive. `algorithm` is a free string, `"ed25519"`, which is the
only kind of key this generator produces (`XrplVanity.Core.Seed` encodes the `sEd…` family seed and
nothing else). `description` becomes the wallet's label on the phone and carries the pattern that
matched. `folder` is optional and is a folder **name**, not an id: the wallet matches it against
its own folders without regard to case and creates one when nothing matches, so a name is all this
side can know. It is capped at 64 characters here because a longer one is not refused on that side
— it is ignored, and the wallet lands in the default folder without saying so. `keyValue`,
`classicAddress`, `keyType` and `algorithm` are required by the importer; everything else may be
left out.

The pipeline, in order:

1. serialize the JSON, UTF-8;
2. **raw DEFLATE** (RFC 1951, no zlib wrapper) — `DeflateStream` at `CompressionLevel.Optimal`;
3. with a password: `PBKDF2-HMAC-SHA256(UTF-8 password, 16-byte random salt, 600000 iterations)` to
   a 32-byte key, then AES-256-GCM over the compressed bytes with the 26-byte header as associated
   data, producing a 12-byte nonce, the ciphertext and a 16-byte tag;
4. frame the container as above;
5. Base45 (RFC 9285) over the container bytes.

`kdfId` is `1` for PBKDF2-HMAC-SHA256 and `0` for no password. The value is authenticated as part
of the associated data, so it has to match what was used to derive the key.

Base45 is not decoration: both mobile scanners hand a *string* back to the wallet, bytes above
0x7F do not survive that, and the Base45 alphabet is exactly the QR alphanumeric character set,
which packs 5.5 bits per character instead of 8 bits per byte. Every container starts with the four
bytes `SBW1`, which encode as the characters `TNA11B` — the prefix the wallet's parser looks for
before it decodes anything. The prefix comes from the magic alone, so a password-less container
carries it too and scans the same way.

### A bare seed

The `sEd…` string itself, nothing around it. The StaticBit wallet does **not** import this — its
scanner recognises a WalletConnect URI or an XRPL address and answers "Unrecognized QR code" to
anything else — and the dialog says so where the choice is made. It is there for moving one key
into a wallet that reads a family seed directly, and for reading a key across an air gap without a
file.

### Which one is offered

The backup container always. The bare seed only when the dialog was opened for exactly one wallet;
with several selected the choice is disabled and names the reason.

## Where it opens

A modal dialog over the page it was opened from, reusing the `.modal-backdrop` / `.modal-box`
chrome and the `inert` treatment the delete confirmation already uses. Two entry points, both in
`ResultsList` and therefore on the Search page and on History alike:

- a QR button in a result row, next to the one that reveals the seed — one wallet;
- `Show QR…` in the selection bar next to `Export selected…` — every selected row.

The dialog belongs to the **page**, not to `ResultsList`: everything inside `.content-scroll`
is covered by the `inert` attribute the page puts on it, and a dialog rendered from in there
would make itself inert along with the page behind it. `ResultsList` therefore raises a request
carrying the rows, exactly as `PatternEditor` raises its request to clear the pattern set, and the
page owns the dialog and the state behind it.

The dialog's order is deliberate. The format switch and an optional password come first, and
nothing secret is drawn until **Show code** is pressed: the same shape as the wallet, where the
code appears only after the user asks for it.

Below the code sit the caption "Scan with StaticBit on your phone." and two links:

- App Store — `https://apps.apple.com/us/app/staticbit/id6745529426`
- Google Play — `https://play.google.com/store/apps/details?id=io.staticbit.wallet`

They are plain `<a href>` elements. MAUI's `BlazorWebView` hands an `http(s)` navigation to the
system browser, which is how the estimate card's "reference rates" link already behaves.

## How the code is drawn

`QRCoder` (MIT, no dependencies of its own) produces the module matrix; the component renders it as
**inline SVG**. Not `QRCoder-ImageSharp`, which the wallet uses: that pulls ImageSharp in to
produce a PNG this design has no use for.

Correction level **H**, and the encoding mode is left to the library. The Base45 alphabet is the QR
alphanumeric set, so the encoder chooses alphanumeric on its own; forcing UTF-8 would drop it to
byte mode and cut the capacity by a third.

The StaticBit mark is drawn over the middle of the code — 20% of its width, on a white pad so it
never sits directly on the modules. A logo is damage the error correction has to absorb, so past
**1763** characters it is dropped rather than drawn: that is the wallet's own threshold, and
neither side ever lowers the correction level to make a mark fit. The asset is the wallet's
`96_QR.png`, copied in as `wwwroot/images/staticbit-qr-logo.png`.

The code is drawn on a light plate regardless of the application's dark theme — a QR code inverted
against a dark background is not reliably scannable.

## Security

- **Inline SVG, never an `<img>` and never a PNG.** The wallet draws its backup code into a
  `<canvas>` for the same reason: a long press on an image in WKWebView offers "Add to Photos",
  which would put a password-less code into the phone's gallery. WebView2 has the same class of
  leak in "Save image as…", and an inline SVG offers that menu nothing.
- **A password-less code is warned about**, in the wallet's own terms: the code is not encrypted,
  and whoever photographs it owns the keys.
- **The code hides itself after 60 seconds**, in both formats, with a button to show it again. The
  existing `SeedRevealSeconds` setting (15 s by default) is too short to line up a phone camera, so
  this is a constant of its own rather than a new setting.
- **The container bytes and the derived key are zeroed** with `CryptographicOperations.ZeroMemory`
  as soon as the Base45 text has been produced from them; the password bytes go the same way. What
  the dialog keeps while it is open is the module matrix it draws, so **Show again** redraws
  without rebuilding anything, and closing the dialog drops that too.
  This is honest but partial, and the design does not pretend otherwise: the Base45 text and the
  seed itself are `string`s — the seed lives in `SessionResult` as one — and a .NET string cannot
  be zeroed. The process holds the seed either way; what this rule buys is that the QR path leaves
  behind no *additional* copy once the dialog is closed.
- **Nothing is written to disk.** There is no "save as PNG" and no share, so the wallet's rule that
  a password-less container may only ever be shown on screen is satisfied by construction rather
  than by a check.

## Capacity

One QR code at level H holds 1852 Base45 characters, which is 1234 container bytes. Measured
against this payload shape with random base58 keys, that is:

| | no password | password |
|---|---|---|
| without `description` | 23 wallets | 22 wallets |
| with `description` | 22 wallets | 21 wallets |

`description` is always written, so the second row is what this application actually offers; the
first is there to show what the label costs, which is about one wallet. A folder name costs nothing
measurable on top of that — the same name repeats in every entry, which is exactly what DEFLATE is
good at — so the two figures hold with or without one.

The implementation asserts these in a test rather than trusting the table: DEFLATE's output depends
on the exact data, and .NET's `CompressionLevel.Optimal` is not obliged to match the tool the
measurement was taken with. If the measured figure differs, the test is what moves — the numbers
here are a claim about the shape of the payload, not a target to code against.

Past the limit the dialog refuses and says how many of the selected rows would fit. Splitting
across several codes is not built, and the correction level is never lowered to make something fit
— both match the wallet.

## The dialog grows downwards

Almost everything in the dialog is conditional — the note is one line or three, the backup's
fields are meaningless for a bare seed, the code and its warning come and go — and a centred box
moves as a whole every time any of that changes.

The answer is not to reserve the room. Reserving it makes the dialog permanently as tall as its
tallest state, which here is 700px of mostly empty box that does not fit the window: it gains a
scrollbar, and the format buttons or the Close button are off screen when it opens. That was
tried, and it was worse than the problem.

Instead the dialog is anchored 24px from the top rather than centred. Content then extends
downwards, so a row that appears pushes down only what is below it and everything already read
stays where it was, and each state is exactly as tall as it needs: measured at 248px for a bare
seed, 320px for the backup form, 619px once a code is on screen, none of them scrolling in a
720px window.

## Behaviour at the edges

- An empty selection cannot open the dialog; the button is disabled, as `Export selected…` is.
- Switching the format clears a code that is already on screen: the two payloads are different
  secrets and the picture must never lag behind the switch.
- A folder name is trimmed, and a blank one is left out of the payload rather than written empty.
- The password and its confirmation must agree before **Show code** does anything; the bare seed
  ignores both fields entirely, since there is nothing to encrypt.
- Closing the dialog drops the container and the rendered code. Reopening rebuilds them.
- Pressing **Show code** with a password takes a noticeable moment — 600 000 PBKDF2 iterations — so
  the button reports that it is working and is disabled while it does.

## What is deliberately not built

- **Scanning.** This is export only; the application has no camera path and no reason for one.
- **Multi-frame QR** for a selection that does not fit, and any lowering of the correction level.
- **Saving or sharing the code as a file.** See Security.
- **secp256k1 keys.** The generator derives Ed25519 only, so `algorithm` is a constant here.

## The compatibility risk

The container format is StaticBit's own and carries a version byte that is `1` today. Nothing tells
this application when that changes; a wallet that starts writing version 2 would still scan our
version 1 code only for as long as its importer accepts it. Two things keep that honest: the
byte-level tests below pin every field this design depends on, and the smoke checklist carries a
manual step that decodes a produced code with the wallet repository's own offline decoder
(`tools/backup-decoder/index.html`) and then scans it with a phone.

The wallet also re-derives each entry from `keyValue` + `algorithm` and compares the result with
`classicAddress`, skipping an entry that disagrees. Here both come from the same generated key, so
they cannot disagree — but that check is the reason `classicAddress` is written from the result row
rather than recomputed.

## Testing

- **Base45** against the RFC 9285 vectors: `AB` → `BB8`, `Hello!!` → `%69 VD92EX0`,
  `base-45` → `UJCLQE7W581`.
- **The container**, byte by byte: the magic, the version, both `kdfId` values, the big-endian
  iteration count, the offsets of salt, nonce, ciphertext and tag, and that the associated data is
  exactly the first 26 bytes. A test decrypts its own output with the same associated data, and a
  second one inflates it and compares the JSON with the expected text character for character.
- **The password-less frame** is exactly six header bytes followed by the compressed payload.
- **The prefix**: every container this code produces starts with `TNA11B`, with and without a
  password.
- **Capacity**: the container for the numbers in the table above stays within 1234 bytes, and one
  wallet more exceeds it.
- **The folder**: a name reaches every entry in the payload, a blank one reaches none, and one
  longer than 64 characters is cut rather than sent whole.
- **The mark**: it is drawn for a short code, dropped past 1763 characters, and absent when no
  logo is asked for.
- **The component**: no code is rendered before **Show code**; the format switch clears a rendered
  code; the bare seed is disabled for a multi-wallet selection and enabled for one; mismatched
  passwords block the build; the code hides itself after its window and comes back on demand; both
  store links are present with the exact URLs above.
- **Manually, once per release**: decode a produced code with the wallet repository's offline
  decoder, then import it by scanning with the wallet on a phone and confirm the addresses match
  the rows that were selected.
