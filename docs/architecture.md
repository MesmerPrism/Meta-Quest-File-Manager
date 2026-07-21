# Architecture

## Decision

Use a dependency-light .NET core for ADB operations, a CLI for complete
automation parity, and a thin Windows WPF projection. The first release is a
focused file and APK transfer tool, not another general Quest runtime console.

## Scope

- ADB tool discovery and bounded process execution.
- Serial-scoped device discovery and operations.
- Browsing, pulling, and explicit pushing on shell-accessible paths.
- Third-party package listing, single-APK export, hashing, single-APK install,
  and atomic folder-based split APK set install.
- Explicit Wi-Fi ADB enable/connect/disconnect with no ADB daemon lifecycle.
- Bounded parallel single-APK and complete split-set installation across
  distinct Wi-Fi ADB endpoints, with one result per target.
- Windows GUI and CLI projections.
- Public CI, Pages, release archives, and boundary validation.

## Non-scope

- Protected app-data access, rooting, entitlement bypass, or DRM handling.
- App data, saves, OBB, or asset-pack backup.
- File deletion, package uninstall, clear-data, or ADB daemon lifecycle.
- TLS pairing-code discovery, network scanning, or persistent pairing secrets.
- Bundled Android tools, APK catalogs, private packages, or live evidence.
- Android and Apple host applications in the first release.

## Authority

Android's package manager owns installed package paths. ADB owns transport.
`MetaQuestFileManager.Core` owns safe command construction, parsing, transfer
policy, and export completeness checks. The app and CLI adapt user intent into
that core and do not redefine behavior.

Wi-Fi enablement is a sequenced transaction: read `wlan0` from one USB serial,
run `tcpip` on that same serial, connect one validated endpoint, and verify its
ready device row. Connection establishment is endpoint-scoped because no ADB
serial exists before `connect`; all subsequent device work is serial-scoped.
Parallel installation owns only bounded orchestration. Android package-manager
transactions remain independent per headset.

Rusty Kiosk owns catalog/tag semantics, normal versus guarded launch, the
Accessibility watchdog, and user-facing Wi-Fi setup requests. The file manager
owns ADB transport, optional installation/provisioning, desktop projection, and
reviewed Quest settings. Host control crosses only Kiosk's exported
`android.permission.DUMP` provider. Provider v2 accepts fixed typed commands
and bounded tag chunks; tag chunks are ordered, capped at 256 KiB, SHA-256
verified, schema validated, and atomically activated. No host path, component,
intent action, or shell text crosses that boundary.

## Interfaces

`ICommandRunner` is the external-process boundary. `AdbClient` exposes device,
file, and package routes. `OperatorCommand` is the shared human-operator
contract: its immutable inputs produce both the CLI argument vector and the
core execution request. Arguments remain structured until they reach the
process API. Remote shell paths use one audited POSIX quoting helper.

`OperatorProgress` is a separate optional projection contract. Core operations
own honest work units; WPF displays them without changing command authority.
Zero total units means indeterminate. CLI JSON remains a stable final result
document and is not interleaved with transient progress events.

`OperatorMutationReceipt` is the result contract for mutations. Its operation
identity, desired state, observed state, transition history, and readback flag
are shared by WPF and CLI. Dispatch records `sent`; the operation then remains
`pending`; only command-specific evidence can produce `confirmed`. Wi-Fi prompt
admission stays pending until a later Kiosk status reports enabled. Five-minute
non-matches become timed out but remain reconcilable on later refresh.

The CLI is the contract surface for agents and future GUI, Android-host, and
Apple-host adapters. Any new GUI action must first have an equivalent typed
command, CLI route, optional PowerShell rendering for tests/docs, and parity
test. Automation details stay out of the non-technical WPF interface.

## Observability

Every command returns exit code, standard output, standard error, and elapsed
time. User-facing surfaces show condensed failures without hiding the ADB
message. APK export additionally records local size and SHA-256.
Wi-Fi routes retain the verified endpoint and device row. Parallel routes
retain the deterministic APK path set, concurrency cap, and one command result
or exception summary per target, including partial failures.
The WPF footer shows active status for all operations, three owned Wi-Fi phases,
and completed-target progress for fan-out. It does not invent byte or remaining-
time percentages from ADB prose. The Rusty Kiosk tab additionally shows the
latest PC/headset synchronization receipt rather than optimistic button state.

Future diagnostics bundles will record tool version, command goal, selected
serial placeholder, result class, and artifact types while keeping raw device
evidence local.

## Validation

- Unit tests use a fake process runner and never require a headset.
- Operator-contract tests cover every WPF operation from its exact CLI
  arguments through the serial-scoped ADB projection.
- Parsers cover ready, unauthorized, and offline devices; file paths with
  spaces; package lists; single APKs; and split APK rejection.
- Bundle tests prove one deterministic top-level APK set becomes one
  serial-scoped `install-multiple` invocation.
- Wi-Fi tests prove address inspection precedes transport mutation, explicit
  confirmation is required, and the exact connected endpoint is verified.
- Parallel tests prove the concurrency cap, target de-duplication,
  serial-scoped calls, complete bundle fan-out, and partial-failure retention.
- Progress tests prove explicit indeterminate state, bounded percentage
  derivation, ordered Wi-Fi phases, and exact parallel target completion.
- Mutation tests prove sent/pending/confirmed ordering, wearer-prompt pending
  behavior, later status reconciliation, CPU/GPU property readback, and bounded
  SHA-256 tag transfer without raw Android-data paths.
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
| Partial bundle install | Snapshot at least two top-level APK paths and pass the complete set to one `install-multiple` operation. |
| Shell injection | Validate serial/package input and quote remote paths with one helper. |
| Hidden device mutation | Require confirmation plus a sent/pending/readback-confirmed receipt; omit delete/uninstall. |
| Optimistic state after a prompt | Keep the receipt pending until later headset status matches. |
| Scoped-storage drift breaks tag files | Transfer fixed provider chunks, verify SHA-256/schema, then atomically hotload. |
| Optional Kiosk breaks file tools | Keep Kiosk detection and commands isolated to its tab/routes. |
| Hidden Wi-Fi/daemon mutation | Require approval, scope `tcpip` to one USB serial, scope connect/disconnect to one endpoint, and never reset the ADB server. |
| Unbounded or ambiguous fan-out | Require two distinct Wi-Fi serials, cap concurrency at 16, and retain every target result. |
| Misleading progress | Use only owned phase/target totals; show every other ADB operation as indeterminate. |
| Misleading backup claim | State clearly that APK export excludes data and assets. |
| Public evidence leak | Ignore artifacts and scan tracked files before publication. |
| Toolchain drift | Discover ADB explicitly and report the selected executable. |

## Next Slice

Add diagnostics bundles, richer remote metadata, verified folder transfer,
and optional TLS pairing only after a separate authority review. Split APK
export remains a later explicit format with all parts and a manifest installed
as one set.
