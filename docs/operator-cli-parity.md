# GUI And CLI Operator Parity

Every device operation in the WPF app is represented by one immutable
`OperatorCommand` in the core library. The WPF button and CLI both construct
that command through the same factory and send it through the same executor.
The CLI is intended for agents and automation, so command text is deliberately
not projected into the non-technical WPF interface.

The Windows release places these programs beside each other:

```text
MetaQuestFileManager.exe
meta-quest-file-manager.exe
```

The CLI can be invoked directly in PowerShell without translating GUI labels
into a different automation model. Agents can include `--adb <path>` when an
exact tool selection is part of the test.

## Operation Map

| WPF operation | Equivalent CLI route |
| --- | --- |
| Refresh headsets | `devices` |
| Open, go up, or open a folder | `files list` |
| Pull selected file | `files pull` |
| Push file here | `files push` |
| Refresh packages | `apk list` |
| Export selected package | `apk export` |
| Install on selected headset | `apk install` |
| Install APK bundle | `apk install-bundle` |
| Enable and connect Wi-Fi ADB | `wifi enable` |
| Connect an enabled Wi-Fi headset | `wifi connect` |
| Disconnect selected Wi-Fi headset | `wifi disconnect` |
| Install one APK on checked Wi-Fi headsets | `apk install-many` |
| Install one APK bundle on checked Wi-Fi headsets | `apk install-bundle-many` |

Example shapes use placeholders rather than live device or local identities:

```powershell
& '.\meta-quest-file-manager.exe' files list --serial <quest-serial> --path /sdcard --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' files pull --serial <quest-serial> --remote /sdcard/Download/example.txt --output <local-path> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' files push --serial <quest-serial> --file <local-path> --remote /sdcard/Download/example.txt --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk list --serial <quest-serial> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk export --serial <quest-serial> --package <package> --output <local-apk> --overwrite --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk install --serial <quest-serial> --file <local-apk> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk install-bundle --serial <quest-serial> --folder <apk-folder> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' wifi enable --serial <usb-serial> --port 5555 --confirm-wifi-adb --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' wifi connect --host <quest-ip> --port 5555 --confirm-wifi-adb --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' wifi disconnect --host <quest-ip> --port 5555 --confirm-wifi-adb --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk install-many --serial <quest-a-ip>:5555 --serial <quest-b-ip>:5555 --file <local-apk> --parallelism 2 --json --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk install-bundle-many --serial <quest-a-ip>:5555 --serial <quest-b-ip>:5555 --folder <apk-folder> --parallelism 2 --json --adb <path-to-adb>
```

PowerShell rendering single-quotes paths when required and doubles embedded
single quotes. ADB receives an argument list through `ProcessStartInfo` rather
than a shell command string.

The WPF confirmation dialog is projected as `--confirm-wifi-adb`. The CLI
rejects every Wi-Fi state change when that operator-approval marker is absent.
Agents must not add the flag without approval for the exact target. Parallel
commands repeat `--serial` once per checked headset and return all per-target
results even when the process exit status is nonzero.

The WPF footer's progress bar is a transient projection of the same executor,
not a separate operation. CLI arguments therefore remain identical. Machine-
readable CLI output stays one final JSON document; agents use its per-target
results rather than scraping GUI animation or mixed progress lines.

## Acceptance

`OperatorCommandTests` must cover the exact CLI argument vector for every WPF
operation, PowerShell quoting, and execution through the shared dispatcher into
serial-scoped ADB calls. Bundle validation additionally proves that every
top-level APK is sent in one deterministic `install-multiple` call. Live
validation then runs the same CLI routes against one explicitly selected
authorized headset; raw serials, package names, APKs, and evidence remain local
and ignored.

Wi-Fi and parallel acceptance additionally proves address inspection occurs
before `tcpip`, no daemon lifecycle command is emitted, each install remains
serial-scoped, concurrency is bounded, duplicate targets are rejected, and
partial failures remain visible.
