# Contributing

Contributions are welcome. Keep changes focused, public-safe, and reversible.

Before opening a pull request:

```powershell
dotnet build MetaQuestFileManager.slnx --configuration Release
dotnet test MetaQuestFileManager.slnx --configuration Release
pwsh -NoProfile -File ./tools/Test-PublicBoundary.ps1
```

Device-facing changes need a no-device test with a fake command runner before
live validation. Never attach raw device logs, serials, private package names,
or APK files to a public issue.
