# Meta Quest File Manager

Meta Quest File Manager is a Windows-first desktop application for browsing
the ADB-accessible storage on a Meta Quest headset, copying files in either
direction, installing user-supplied APKs, and exporting sideloaded single-APK
apps back to a Windows file.

The repository also ships a CLI over the same core operations so every GUI
action can be automated and tested.

> This is an independent open-source project. It is not affiliated with or
> endorsed by Meta. Meta Quest is a trademark of Meta Platforms, Inc.

## Current First Slice

- discover USB or already-connected ADB devices;
- browse absolute device paths, starting at `/sdcard`;
- pull a selected file to Windows;
- push an explicitly selected Windows file to the current device folder;
- list third-party Android packages;
- export an installed app when Android reports exactly one APK path;
- write a SHA-256 sidecar for every exported APK;
- reject split APK packages with an actionable explanation;
- install a user-supplied APK with explicit reinstall, downgrade, runtime
  permission, and test-package options;
- expose the same routes through a Windows WPF app and CLI.

The app does not copy app data, saves, OBB folders, downloaded asset packs, or
store entitlements. ADB only exposes paths permitted to the Android shell
user; this is not unrestricted access to the entire headset filesystem.

## Requirements

- Windows 10 version 2004 or later;
- .NET 10 SDK for source builds;
- Android SDK Platform Tools (`adb`);
- a Meta Quest with Developer Mode enabled and this computer authorized for
  USB debugging.

ADB is located in this order:

1. `META_QUEST_FILE_MANAGER_ADB`;
2. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`;
3. `%ANDROID_SDK_ROOT%` or `%ANDROID_HOME%`;
4. `adb` on `PATH`.

## Build

```powershell
dotnet build MetaQuestFileManager.slnx
dotnet test MetaQuestFileManager.slnx
dotnet run --project src/MetaQuestFileManager.App
```

## CLI

```powershell
dotnet run --project src/MetaQuestFileManager.Cli -- devices
dotnet run --project src/MetaQuestFileManager.Cli -- files list --serial <quest-serial> --path /sdcard
dotnet run --project src/MetaQuestFileManager.Cli -- files pull --serial <quest-serial> --remote /sdcard/Download/example.txt --output ./example.txt
dotnet run --project src/MetaQuestFileManager.Cli -- files push --serial <quest-serial> --file ./example.txt --remote /sdcard/Download/example.txt
dotnet run --project src/MetaQuestFileManager.Cli -- apk list --serial <quest-serial>
dotnet run --project src/MetaQuestFileManager.Cli -- apk export --serial <quest-serial> --package com.example.app --output ./com.example.app.apk
dotnet run --project src/MetaQuestFileManager.Cli -- apk install --serial <quest-serial> --file ./example.apk
```

Pass `--json` to list commands for machine-readable output. Pass `--adb` to
select an explicit ADB executable without changing global machine settings.

## Design And Safety

- [Architecture](docs/architecture.md)
- [ADB scope and safety](docs/adb-scope-and-safety.md)
- [Release workflow](docs/release-workflow.md)
- [Reference intake](docs/reference-intake.md)

## Roadmap

1. Finish the Windows transfer and package-management experience.
2. Add diagnostics bundles and no-device UI verification.
3. Add privately signed Windows delivery, a guided installer, and update
   checks using GitHub Pages plus GitHub Releases.
4. Add split-APK set export/install with a manifest and exact-set validation.
5. Define portable contracts for future Android and Apple host clients.

## License

MIT. See [LICENSE](LICENSE). Android Platform Tools and other optional external
tools retain their own licenses and are not included in this source tree.
