# ADB Scope And Safety

Meta Quest File Manager assumes Developer Mode is already enabled and the host
has been authorized in-headset. It does not enable Developer Mode or bypass
Android permissions.

## What File Browsing Means

The app starts at `/sdcard`, the user-visible shared-storage surface. Other
absolute paths may be entered, but Android decides whether the `shell` user can
list or read them. A failure is reported as a permission or path error; the app
does not attempt privilege escalation.

The initial feature set is deliberately asymmetric:

- list is read-only;
- pull copies from Quest to the selected Windows path;
- push copies one explicitly selected Windows file to an explicit remote path;
- delete and recursive mutation are absent.

## APK Export

The export route runs Android package-manager inspection equivalent to:

```text
adb -s <quest-serial> shell pm path <package>
```

One returned path is exported and hashed. More than one path means the install
uses split APKs; the app refuses it because exporting only `base.apk` would be
an incomplete backup.

APK export does not include app data, saves, login state, OBB files, downloaded
assets, licenses, or entitlements. Only export software you own or are allowed
to copy.

## APK Install

Reinstall is enabled by default. Downgrade, runtime permission grants, and
test-only APK admission are separate explicit options. Install errors retain
the Android failure code so signing, version, ABI, or storage problems remain
diagnosable.

The bundle route reads at least two top-level `.apk` files from one selected
folder, orders them deterministically, and passes the entire set through one
`adb install-multiple` request. It does not recurse, and it does not treat a
folder of unrelated standalone apps as a batch queue. Android rejects package
name, version, signing-certificate, or required-split mismatches without a
partial per-file install loop.

## Multiple Devices

Every operation is sent with `adb -s <quest-serial>`. The app does not rely on
ADB's implicit single-device selection.

Parallel installation is not an exception to serial scope. The app validates
at least two distinct Wi-Fi ADB serials, bounds concurrent work, and launches
one independently scoped `install` or `install-multiple` request per headset.
Every target receives a result even when another target fails.

## Wi-Fi ADB

Wi-Fi ADB still requires Developer Mode and prior in-headset authorization.
The reviewed enable route starts from one selected, ready USB headset:

1. inspect `ip route` on that serial and select the non-loopback `wlan0` IPv4
   source address;
2. run `tcpip <port>` on that same serial;
3. connect only the validated `<quest-ip>:<port>` endpoint;
4. require that exact endpoint to appear ready in device discovery.

The WPF app asks for confirmation before enable, connect, and disconnect. The
CLI requires `--confirm-wifi-adb`, which an agent may use only after operator
approval for the exact target. Connect and disconnect are endpoint-scoped
because they create or remove the serial itself. They do not reset, restart,
or otherwise manage the global ADB server.

The first Wi-Fi slice does not implement TLS pairing codes, subnet scanning,
or credential storage. A connection can disappear after a headset reboot,
network change, debugging timeout, or authorization revocation. See
[Wi-Fi ADB and parallel installation](wifi-adb-and-parallel-install.md) for the
full workflow and validation contract.
