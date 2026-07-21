# Rusty Kiosk Integration And Synchronization

Rusty Kiosk is an optional, separately licensed Android application bundled by
official Windows releases for convenient installation. Meta Quest File Manager
continues to browse files, transfer APKs, and manage ordinary ADB connections
when the bundle is absent or Kiosk is never installed.

## First-Use Flow

1. Enable Quest Developer Mode through Meta's supported account/device flow.
2. Connect the headset over USB-C and approve this computer's ADB key in-headset.
3. Open the Rusty Kiosk tab and choose **Install and provision (USB)**.
4. The file manager installs the same-signer setup helper, grants only that
   helper `WRITE_SECURE_SETTINGS`, installs Kiosk, and reads back both package
   and permission states.
5. Choose **Request Wi-Fi ADB**. Meta shows its own permission prompt. The PC
   receipt remains pending until the wearer accepts and Kiosk reports Wi-Fi ADB
   enabled.
6. Optionally enable **Ask after restart**. After a reboot, Kiosk can request
   Meta's Wi-Fi ADB allowance again; it cannot accept that allowance for the
   wearer. USB-C remains the recovery path if no ADB transport is reachable.
7. Enable Accessibility only when guarded launches are wanted. It is a separate
   explicit choice and can be disabled again from either surface.

## Desktop Functions

The tab displays the complete Kiosk catalog, including tag-file entries named
for apps not installed on the current headset. Search matches app name, package,
or tag. Tag filtering, tag add/remove, normal launch, and guarded launch use the
same Kiosk command semantics as the headset panel.

Tag files use `rusty.kiosk.app_tags.v1`. Entries may identify an app by name
without a package. Import/export uses provider chunks rather than direct access
to `/sdcard/Android/data`: each chunk is bounded, the complete file is capped at
256 KiB, SHA-256 is checked, the schema is parsed, and activation is atomic.

## Authority Boundary

The release host surface is
`content://io.github.mesmerprism.rustykiosk.operator`, schema
`rusty.kiosk.host_operator.v2`, protected by caller-held
`android.permission.DUMP`. The host can admit only the fixed Kiosk command enum,
poll a matching request result, and transfer the fixed tag document. It cannot
supply shell text, Android components, intent actions, setup endpoints, or
headset paths.

Kiosk retains ownership of launch and watchdog behavior. The setup helper owns
the small secure-settings operations. The Windows app owns ADB transport and
operator confirmation.

## Sent, Pending, Confirmed

Every PC-originated mutation has an operation ID and transition history:

- `sent` records what was requested and where it was sent;
- `pending` means effective-state evidence has not matched yet;
- `confirmed` requires route-specific headset readback;
- `failed` records an explicit error;
- `timed_out` means no match was seen within five minutes, but later refreshes
  may still reconcile a wearer prompt.

Examples of confirmation evidence include Kiosk's guard/accessibility/Wi-Fi/tag
state, same-signer permission readback, remote file size, refreshed package
inventory, the exact connected ADB endpoint, Quest power state, and Oculus
CPU/GPU properties. A displayed Meta permission prompt is never itself treated
as enabled state.

## Distribution

The Windows repository is MIT-licensed. Rusty Kiosk is a separate
AGPL-3.0-or-later work. Official Windows packages aggregate its release-signed
APK pair with the Kiosk license, source URL/revision, and SHA-256 manifest. The
release build rejects debug Kiosk bundles.
