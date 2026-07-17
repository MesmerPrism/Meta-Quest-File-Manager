# Agent Notes

This is a public MIT-licensed repository. Keep every committed file portable
and public-safe.

## Product Boundary

Meta Quest File Manager is a Windows-first operator tool for ADB-authorized
Meta Quest headsets. It owns file-transfer UX, installed-package inspection,
single-APK export, user-supplied APK installation, diagnostics, and Windows
delivery. It does not own Quest runtime behavior, bypass Android permissions,
or promise access to protected app data.

The GUI and CLI must invoke the same typed `OperatorCommand` routes. Every GUI
operation displays a copyable PowerShell command built from the same immutable
arguments it executes. UI handlers collect inputs, invoke those routes, and
display structured results; they must not hide ADB or filesystem business
logic.

## Public Boundary

Do not commit device serials, private package names, APKs, device captures,
raw logs, signing keys, certificates, local absolute paths, downloaded Android
tools, or generated release artifacts. Use placeholders such as
`<quest-serial>`, `<package>`, `<remote-path>`, and `<path-to.apk>` in docs.

Third-party tools remain under their upstream terms. Do not bundle Android
Platform Tools until its redistribution and update path is reviewed and
documented.

## Device Safety

- Use serial-scoped ADB for every device operation.
- Read-only probes come before mutations.
- Initial file management is limited to list, pull, and explicit push.
- Do not add delete, uninstall, clear-data, ADB server lifecycle, Wi-Fi ADB,
  power, or proximity operations without a separate safety and UX review.
- APK export supports one installed APK path and rejects split packages rather
  than producing an incomplete backup.
- A copied APK does not include app data, OBB files, downloaded assets, or
  store entitlement.

## Build And Validation

Use PowerShell 7.6 or newer through `pwsh` for maintained scripts.

```powershell
dotnet build MetaQuestFileManager.slnx --configuration Release
dotnet test MetaQuestFileManager.slnx --configuration Release
dotnet run --project src/MetaQuestFileManager.Cli -- --help
pwsh -NoProfile -File ./tools/Test-PublicBoundary.ps1
```

Run the app:

```powershell
dotnet run --project src/MetaQuestFileManager.App
```

## Architecture

- `MetaQuestFileManager.Core` owns process execution, ADB discovery, command
  construction, typed operator commands, output parsing, transfers, APK
  install/export, and hashes.
- `MetaQuestFileManager.Cli` is the automation-equivalent operator surface.
- `MetaQuestFileManager.App` is the Windows WPF projection.
- Keep external processes behind `ICommandRunner` and preserve cancellation
  and bounded timeouts.
- Use `ProcessStartInfo.ArgumentList`; never construct a host shell command.
- Keep future Android and Apple clients as adapters over explicit contracts,
  not as reasons to put platform UI into the core.
- A GUI/CLI parity test must cover every WPF operation before a new button is
  accepted.

## Release Posture

GitHub Pages is the human-facing download surface and GitHub Releases is the
binary source of truth. The initial workflow publishes portable Windows app
and CLI archives plus checksums. Private signing, the guided installer, and
automatic update adoption require an explicit release unit and configured
repository secrets; never commit private certificate material.
