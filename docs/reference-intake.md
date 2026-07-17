# Reference Intake

## APK Transfer Between Devices conversation

- Why it matters: established the concrete goal of retrieving sideloaded,
  non-store, single-APK apps from Quest through ADB.
- Lesson borrowed: use `pm path`, require exactly one returned APK path for a
  single-file export, pull and hash that file, and install a complete user-held
  split set together rather than treating only `base.apk` as sufficient.
- Overreach rejected: no store-app redistribution, entitlement bypass, app-data
  backup claim, or assumption that a Quest APK runs on unrelated Android
  hardware.
- Target: package inspection and export in `MetaQuestFileManager.Core`.
- Validation: fake-runner unit tests plus a later serial-scoped live smoke.

The original transcript remains private; this public note contains only the
sanitized implementation requirements.

## Rusty XR Companion Apps

- Reference: <https://github.com/MesmerPrism/Rusty-XR-Companion-Apps>
- Why it matters: existing public WPF/CLI Quest operator tool.
- Lesson borrowed: one reusable command layer, explicit ADB discovery, GUI/CLI
  parity, no-hardware diagnostics, and public boundary checks.
- Overreach rejected: no broker, camera, casting, runtime-profile, LSL, or APK
  catalog scope in this focused file manager.
- Validation: shared-core tests and CLI smoke.

## Viscereality Companion

- Reference: <https://github.com/MesmerPrism/ViscerealityCompanion>
- Why it matters: proven GitHub Pages plus Releases delivery, private signing,
  guided setup, App Installer fallback, and machine verification pattern.
- Lesson borrowed: stable latest-download links, separate helper/MSIX trust
  checks, portable fallback, checksums, and clean-machine launch validation.
- Overreach rejected: no study-specific packages, profiles, private payloads,
  or app identities.
- Validation: future signed-release unit.

## Meta Quest Agent Workflow

- Reference: <https://github.com/MesmerPrism/meta-quest-agent-workflow>
- Why it matters: public device-operation and evidence discipline.
- Lesson borrowed: serial-scoped ADB, read-only probes first, bounded commands,
  and private raw device evidence.
- Overreach rejected: no ADB authorization bypass or claim of unrestricted
  filesystem access.
- Validation: public boundary scan and later explicit live-device receipt.
