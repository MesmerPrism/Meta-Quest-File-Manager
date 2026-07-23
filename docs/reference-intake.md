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
- Target: package inspection and export in `QuestIonAbleFileManager.Core`.
- Validation: fake-runner unit tests plus a later serial-scoped live smoke.

The original transcript remains private; this public note contains only the
sanitized implementation requirements.

## Rusty XR Companion Apps

- Reference: <https://github.com/MesmerPrism/Rusty-XR-Companion-Apps>
- Why it matters: existing public WPF/CLI Quest operator tool.
- Lesson borrowed: one reusable command layer, explicit ADB discovery, GUI/CLI
  parity, no-hardware diagnostics, public boundary checks, headset/controller
  power parsing, reversible keep-awake, and explicit CPU/GPU overrides.
- Later refinement: every mutation now carries desired-versus-observed state;
  fixed settings do not become successful merely because their shell process
  exited successfully.
- Overreach rejected: no broker, camera, casting, runtime-profile, LSL, generic
  shell console, or private APK catalog.
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

## Rusty Kiosk

- Reference: <https://github.com/MesmerPrism/Rusty-Kiosk>
- Why it matters: owns the Spatial SDK catalog/tag panel, same-signer setup
  helper, explicit Wi-Fi permission UX, and soft Accessibility watchdog.
- Lesson borrowed: expose a narrow versioned host adapter over the same typed
  command queue as wearer/CLI input; keep Meta permission prompts visible and
  the watchdog inactive inside Kiosk itself.
- Boundary added: DUMP-protected provider v2, fixed commands, and bounded
  ordered tag chunks with SHA-256/schema/atomic activation.
- Overreach rejected: no arbitrary shell, component, intent, filesystem path,
  silent Meta prompt acceptance, device-owner lock task, or dependency from the
  normal file-manager tabs onto Kiosk installation.
