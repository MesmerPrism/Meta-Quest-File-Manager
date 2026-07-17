# Wi-Fi ADB Parallel Install Live Validation — 2026-07-17

## Scope

This is a sanitized public receipt for the first two-headset live validation of
the Wi-Fi ADB and bounded parallel APK installation routes. Raw device serials,
network addresses, device output, and APK bytes remain local and are not part
of this repository.

The operator explicitly approved:

- changing Wi-Fi ADB state on both connected test headsets;
- installing one benign locally generated test APK on both headsets; and
- removing only that test package after verification.

## Environment Summary

| Property | Result |
| --- | --- |
| Ready USB headsets before enablement | 2 |
| Ready Wi-Fi ADB endpoints before enablement | 0 |
| TCP port | 5555 |
| Test artifact | Signed, locally generated public minimal debug APK |
| Source state | Current local source worktree; no publication performed |

## Result

| Gate | Result |
| --- | --- |
| USB-scoped Wi-Fi enable transactions | 2 of 2 passed |
| Distinct ready Wi-Fi endpoints after connect | 2 |
| ADB server reset or restart | No |
| Parallelism limit | 2 |
| Parallel install target results | 2 of 2 passed |
| Independent post-install package verification | 2 of 2 passed |
| Serial-scoped test-package removal | 2 of 2 passed |
| Independent post-cleanup absence verification | 2 of 2 passed |
| Other packages touched | 0 |
| Wi-Fi endpoints ready after cleanup | 2 |

## Route Evidence

The live run used the same CLI entrypoints represented by the WPF controls:

```text
wifi enable --serial <usb-serial> --port 5555 --confirm-wifi-adb --json
apk install-many --serial <wifi-serial-a> --serial <wifi-serial-b> --file <test-apk> --parallelism 2 --json
```

Read-only device and package discovery verified the state before and after each
mutation. Cleanup used one explicit serial-scoped Android package removal per
test endpoint because uninstall is intentionally not a product feature.

## Verdict

Pass. The application enabled and verified two distinct Wi-Fi ADB connections,
installed the same APK on both through the bounded parallel route, retained two
successful target results, verified both installs independently, and restored
the package state without touching unrelated packages. Wi-Fi ADB remains
enabled and connected for the operator's continuing use.
