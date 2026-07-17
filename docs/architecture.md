# Architecture

## Decision

Use a dependency-light .NET core for ADB operations, a CLI for complete
automation parity, and a thin Windows WPF projection. The first release is a
focused file and APK transfer tool, not another general Quest runtime console.

## Scope

- ADB tool discovery and bounded process execution.
- Serial-scoped device discovery and operations.
- Browsing, pulling, and explicit pushing on shell-accessible paths.
- Third-party package listing, single-APK export, hashing, and APK install.
- Windows GUI and CLI projections.
- Public CI, Pages, release archives, and boundary validation.

## Non-scope

- Protected app-data access, rooting, entitlement bypass, or DRM handling.
- App data, saves, OBB, or asset-pack backup.
- File deletion, package uninstall, clear-data, power, proximity, ADB daemon
  lifecycle, or Wi-Fi ADB setup in the initial slice.
- Bundled Android tools, APK catalogs, private packages, or live evidence.
- Android and Apple host applications in the first release.

## Authority

Android's package manager owns installed package paths. ADB owns transport.
`MetaQuestFileManager.Core` owns safe command construction, parsing, transfer
policy, and export completeness checks. The app and CLI adapt user intent into
that core and do not redefine behavior.

## Interfaces

`ICommandRunner` is the external-process boundary. `AdbClient` exposes device,
file, and package routes. Arguments remain structured until they reach the
process API. Remote shell paths use one audited POSIX quoting helper.

The CLI is the contract surface for future GUI, Android-host, and Apple-host
adapters. Any new GUI action must first have an equivalent core and CLI route.

## Observability

Every command returns exit code, standard output, standard error, and elapsed
time. User-facing surfaces show condensed failures without hiding the ADB
message. APK export additionally records local size and SHA-256.

Future diagnostics bundles will record tool version, command goal, selected
serial placeholder, result class, and artifact types while keeping raw device
evidence local.

## Validation

- Unit tests use a fake process runner and never require a headset.
- Parsers cover ready, unauthorized, and offline devices; file paths with
  spaces; package lists; single APKs; and split APK rejection.
- CI builds the WPF app, runs the core tests, exercises CLI help, and scans the
  tracked public boundary.
- Live Quest validation is a separate serial-scoped manual gate.

## Reference Lessons

The public Rusty XR Companion proves the usefulness of a shared WPF/CLI core.
Viscereality Companion supplies the long-term Pages, Releases, signing, guided
installer, and verification-harness pattern. The public Meta Quest Agent
Workflow supplies device-operation and evidence boundaries.

The new repo borrows these boundaries and workflow lessons, not app-specific
packages, private behavior, generated binaries, or broad runtime features.

## Mitigation Map

| Risk | Mitigation |
| --- | --- |
| Wrong headset | Require and display the exact ADB serial on every route. |
| Incomplete exported app | Refuse any package with zero or multiple APK paths. |
| Shell injection | Validate serial/package input and quote remote paths with one helper. |
| Hidden device mutation | Keep mutations explicit and omit delete/uninstall from v1. |
| Misleading backup claim | State clearly that APK export excludes data and assets. |
| Public evidence leak | Ignore artifacts and scan tracked files before publication. |
| Toolchain drift | Discover ADB explicitly and report the selected executable. |

## Next Slice

Add diagnostics bundles, richer remote metadata, verified folder transfer,
and the signed guided Windows delivery lane. Split APK export remains a later
explicit format with all parts and a manifest installed as one set.
