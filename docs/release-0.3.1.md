# QuestIonAble File Manager 0.3.1 history

This release was published under the former name **Meta Quest File Manager**.
The name is retained here only as historical release metadata.

## Decision

Release `0.3.1` as a compatibility and installation-reliability patch over the
first distributable `0.3.0` build. It packages the independently published,
release-signed Rusty Kiosk `0.6.4` bundle and retains all existing file, APK,
Wi-Fi ADB, device, Kiosk, direct-link, WPF, and CLI behavior.

## Scope

- verify and package the exact stable Rusty Kiosk `0.6.4` tag, source commit,
  manifest, hashes, byte counts, license/source files, and same-signer APK pair;
- allow up to 60 seconds for a cold first launch after Windows validates a newly
  installed signed package, while keeping the probe bounded and configurable;
- publish an updated signed MSIX, App Installer feed, guided setup, portable
  WPF/CLI archives, checksums, and provenance-bearing validation receipt;
- update the GitHub Pages download surface to identify File Manager `0.3.1` and
  its bundled Kiosk `0.6.4` release.

## Non-scope

- making Kiosk mandatory for ordinary File Manager functions;
- changing Kiosk permissions, setup-helper authority, host command vocabulary,
  or direct-link transport;
- replacing any existing release asset.

## Validation

The release requires current-main CI, Kiosk bundle-verifier self-test, exact
stable Kiosk `0.6.4` tag resolution, signed Windows asset validation, consumer
Windows install/launch validation, and a serial-scoped Quest upgrade/status run
with effective package, signer, helper-readiness, and cleanup readback.
