# GUI And CLI Operator Parity

Every device operation in the WPF app is represented by one immutable
`OperatorCommand` in the core library. The WPF button executes that command and
shows its PowerShell rendering; the CLI parses the displayed arguments back
into the same command factory and sends it through the same executor.

The Windows release places these programs beside each other:

```text
MetaQuestFileManager.exe
meta-quest-file-manager.exe
```

The command shown at the bottom of the WPF window can therefore be copied into
PowerShell without translating GUI labels into a different automation model.
It includes the selected ADB executable so tool discovery cannot silently
select a different binary.

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

Example shapes use placeholders rather than live device or local identities:

```powershell
& '.\meta-quest-file-manager.exe' files list --serial <quest-serial> --path /sdcard --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' files pull --serial <quest-serial> --remote /sdcard/Download/example.txt --output <local-path> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' files push --serial <quest-serial> --file <local-path> --remote /sdcard/Download/example.txt --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk list --serial <quest-serial> --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk export --serial <quest-serial> --package <package> --output <local-apk> --overwrite --adb <path-to-adb>
& '.\meta-quest-file-manager.exe' apk install --serial <quest-serial> --file <local-apk> --adb <path-to-adb>
```

PowerShell rendering single-quotes paths when required and doubles embedded
single quotes. ADB receives an argument list through `ProcessStartInfo` rather
than a shell command string.

## Acceptance

`OperatorCommandTests` must cover the exact CLI argument vector for every WPF
operation, PowerShell quoting, and execution through the shared dispatcher into
serial-scoped ADB calls. Live validation then runs the same CLI routes against
one explicitly selected authorized headset; raw serials, package names, APKs,
and evidence remain local and ignored.
