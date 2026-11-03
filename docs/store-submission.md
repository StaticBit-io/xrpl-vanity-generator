# Publishing to the Microsoft Store

The Store is the primary distribution channel for the desktop application. It signs the
package with its own certificate, so none of the sideloading machinery in
[desktop-app.md](desktop-app.md) applies here: no `app/make-cert.ps1`, no thumbprint, and
nothing for the user to trust by hand.

This page covers the submission. For the sideload package, which is still the way to test a
build locally, see [desktop-app.md](desktop-app.md).

## Prerequisites

- A Partner Center account. A company account publishes under **StaticBit**, which matches
  the application's branding; an individual account publishes under a personal name.
  Company registration requires business verification and takes noticeably longer than
  individual registration.
- The .NET 10 SDK and the MAUI Windows workload, as for any build of this application.

## 1. Reserve the name and collect the identity values

The Store assigns the package identity, and it is not known until the name is reserved.
In Partner Center, create the app, reserve the name, then open **Product management →
Product identity** and copy three values:

| Partner Center field | Used as |
| --- | --- |
| Package/Identity/Name | `VanityPackageIdentityName` |
| Package/Identity/Publisher | `VanityPackagePublisher` |
| Package/Properties/PublisherDisplayName | `VanityPackagePublisherDisplayName` |

`PublisherDisplayName` must match what Partner Center shows, character for character. A
mismatch is rejected at upload with a manifest validation error, not at certification.

## 2. Build the package

```powershell
dotnet publish src/XrplVanity.App/XrplVanity.App.csproj -c Release -f net10.0-windows10.0.22621.0 -p:PublishProfile=Store-win-x64 -p:VanityPackageIdentityName=<Name> -p:VanityPackagePublisher="<Publisher>" -p:VanityPackagePublisherDisplayName="<PublisherDisplayName>"
```

The package lands in `src/XrplVanity.App/bin/msix-store/XrplVanity.App_<version>_Test/` as
`XrplVanity.App_<version>_x64.msix`, about 74 MB because the build is self-contained. Upload
that file: Partner Center accepts a plain `.msix`.

It is unsigned on purpose — the Store rejects a package signed by anyone else and signs it
itself once the submission is accepted. The `_Test` in the folder name comes from the
sideload packaging layout and means nothing here.

There is no `.msixupload`, and `UapAppxPackageBuildMode=StoreUpload` will not produce one:
that property only works on the UWP packaging pipeline, and a MAUI / Windows App SDK project
runs the standard one, where it is silently ignored. The publish profile documents this so it
is not tried again.

The build prints the identity it stamped:

```
Package identity: Name=..., Publisher=..., Version=...
```

Check that line before uploading. If the substitution ever fails to run, the build stops
with an error rather than producing a package with the wrong identity.

## 3. Versioning

`VanityVersion` in [Directory.Build.props](../Directory.Build.props) is the single source of
truth. It feeds the package version, the assembly version, and the version shown in the
application's About section.

Two Store rules constrain it:

- The fourth component of the package version must be **0**. The Store reserves it. The
  build derives the package version as `$(VanityVersion).0`, so keeping `VanityVersion` a
  three-part number satisfies this automatically.
- Every submission needs a version higher than the last one accepted. A version that has
  already been used cannot be submitted again, even if the submission was withdrawn.

## 4. Store listing

Copy for the listing lives in [store-listing.md](store-listing.md), including the
description, the feature list, and the notes for the certification team.

Screenshots: `docs/images/desktop-search.png` shows the Search page and can be used as-is.
The Store requires at least one screenshot at a minimum of 1366 x 768.

The privacy policy URL is the repository's [PRIVACY.md](../PRIVACY.md):

```
https://github.com/StaticBit-io/xrpl-vanity-generator/blob/main/PRIVACY.md
```

## 5. What certification is likely to ask about

**The application is not a miner.** It generates candidate key pairs and tests their
addresses against a pattern, and it can use the GPU at full load while doing so. That
resembles cryptocurrency mining from the outside, and Store policy prohibits mining on the
user's device. The distinction is real and worth stating plainly in the submission notes:
the application performs no proof-of-work, earns nothing, joins no network, and makes no
network connection at all. It searches for a vanity address the user asked for, and stops
when it finds one.

**It handles secrets.** The application generates seeds and stores them encrypted with
DPAPI under the current user. It never transmits them. See [PRIVACY.md](../PRIVACY.md).

**Full trust.** The package declares `runFullTrust`, which is normal for a packaged desktop
application and required for the GPU backends and the tray icon.

## 6. Known constraints

The package is `win-x64` only. Windows on ARM runs it through x64 emulation, where the CPU
search works and the GPU backends do not. An `arm64` package would need its own
`RuntimeIdentifier` and its own run of the GPU backends; it is out of scope for the first
submission.

## Checklist

1. Partner Center account registered and verified.
2. App created, name reserved, identity values copied.
3. `VanityVersion` set to the version being submitted.
4. Package built with the Store profile and the three identity properties.
5. Identity line in the build output checked against Partner Center.
6. Listing text, screenshots, and privacy policy URL filled in.
7. Age rating questionnaire completed.
8. Submission notes explaining the search-versus-mining distinction.
