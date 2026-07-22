# Meta Quest File Manager 0.3.0

## Decision

Release `0.3.0` as the first public File Manager build that contains the
optional Rusty Kiosk operator surface and the independently published,
release-signed Rusty Kiosk `0.6.1` bundle. This is a minor version because the
public Windows application gains a substantial new optional application and
transport family while preserving existing file, APK, Wi-Fi ADB, and device
routes.

## Scope

- optional Kiosk installation and one-time helper provisioning over authorized
  serial-scoped ADB;
- Kiosk catalogue, tags, normal/guarded launch, and fixed user-control routes;
- authenticated local direct-link commands, bounded staging, and attended
  PackageInstaller sessions;
- exact Kiosk release provenance checks before Windows packaging;
- signed MSIX, guided setup, App Installer feed, portable WPF/CLI archives,
  checksums, and a provenance-bearing validation receipt.

## Non-scope

- making Kiosk mandatory for ordinary File Manager functions;
- silent direct-link APK approval, raw shell, arbitrary headset paths, or
  encrypted direct transport;
- split-package export, Android/Apple host apps, or fleet orchestration.

## Validation

The release requires current-main CI, the Kiosk bundle-verifier tamper test,
exact stable Kiosk `0.6.1` tag resolution, signed Windows asset validation, a
consumer Windows install/launch pass, and a serial-scoped Quest install/status
pass with fresh package and permission readback.
