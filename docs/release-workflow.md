# Release Workflow

GitHub Pages is the stable human-facing install surface. GitHub Releases is the
binary source of truth. Pages links target `releases/latest/download`, so a new
release does not require new website URLs.

## Published Assets

Every Windows preview release contains:

- `MetaQuestFileManager-Setup.exe`: signed guided installer and updater;
- `MetaQuestFileManager-win-x64.msix`: signed Windows package;
- `MetaQuestFileManager.appinstaller`: update feed for the stable package
  identity;
- `MetaQuestFileManager.cer`: public half of the package signing certificate;
- `MetaQuestFileManager-win-x64.zip`: portable WPF app plus operator CLI;
- `meta-quest-file-manager-cli-win-x64.zip`: CLI-only automation archive;
- `SHA256SUMS.txt`: checksums for every release asset;
- `release-validation.json`: signature, timestamp, identity, and feed receipt.

Android Platform Tools are not bundled. The app discovers an operator-supplied
`adb.exe` through the documented search order.

The WPF app, automation CLI, guided setup helper, MSIX package, and GitHub Pages
site use the same folder mark. Its canonical source and multi-resolution ICO
live under `assets/branding`; `tools/app/New-BrandAssets.ps1` regenerates every
committed application and website size, and `tools/app/Test-BrandAssets.ps1`
checks the assets plus embedded EXE resources.

## Consumer Routes

The recommended route is `MetaQuestFileManager-Setup.exe`. It downloads the
public certificate and App Installer feed from the latest GitHub release,
requests Windows administrator approval, trusts the certificate in Local
Machine `TrustedPeople`, installs or updates the stable package identity, and
launches the app.

For machines that block the self-issued helper executable, the manual fallback
is deliberately kept public:

1. download `MetaQuestFileManager.cer` and trust it in **Trusted People**;
2. download and open `MetaQuestFileManager.appinstaller`;
3. if App Installer is unavailable, download and open the signed MSIX;
4. use the portable ZIP when package installation is restricted.

A self-issued certificate can support an explicitly trusted MSIX but does not
guarantee that Smart App Control will admit a downloaded helper executable.
The website must not describe this helper as Smart App Control safe.

## Local Release Validation

PowerShell 7.6 or newer and Visual Studio with the MSIX/Desktop Bridge workload
are required. Export a repository-specific PFX from the private Windows
certificate store into ignored `artifacts/signing`, then run:

```powershell
pwsh -NoProfile -File ./tools/app/Invoke-ReleaseBuild.ps1 `
  -Version <version> `
  -PackageCertificatePath ./artifacts/signing/windows-signing.pfx `
  -PackageCertificatePassword <pfx-password>

pwsh -NoProfile -File ./tools/app/Test-ConsumerInstall.ps1 `
  -ReleaseDirectory ./artifacts/release `
  -RemoveAfterTest
```

The default consumer test exercises the elevated guided route. On an
unattended, non-elevated agent shell, use `-DirectPackage` to validate the
helper's no-change plan and then install the same signed MSIX directly; the
receipt records which route ran and never claims the guided route passed.

The release build preserves the native WAP-produced MSIX, applies SHA-256
Authenticode signatures with RFC 3161 timestamps, verifies the expected
publisher, checks the App Installer identity and stable URLs, inspects the MSIX
payload, and writes checksums. The public validation receipt records release
filenames rather than local or CI-runner build paths. The consumer test stages a local HTTP feed with range
support because the Windows deployment service does not consume workspace file
URIs like a browser download.

The guided setup also exposes a no-change agent route:

```powershell
MetaQuestFileManager-Setup.exe --plan --json
```

`--plan` downloads and validates the release identity without trusting a
certificate or installing a package. Actual guided installation requests UAC;
the elevation is part of the public installer contract.

## GitHub Configuration

The release workflow requires these Actions secrets:

- `WINDOWS_PACKAGE_CERTIFICATE_BASE64`;
- `WINDOWS_PACKAGE_CERTIFICATE_PASSWORD`;
- `WINDOWS_PACKAGE_PUBLISHER`;
- `WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64`;
- `WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD`.

Optional Actions variables select alternate RFC 3161 timestamp services:

- `WINDOWS_PACKAGE_TIMESTAMP_URL`;
- `WINDOWS_PREVIEW_SETUP_TIMESTAMP_URL`.

Private keys stay in the Windows certificate store, ignored local artifacts,
and encrypted GitHub Actions secrets. They are never committed. Publishing a
tag or manually dispatching the workflow builds, validates, uploads, and then
creates or updates the matching GitHub Release.
