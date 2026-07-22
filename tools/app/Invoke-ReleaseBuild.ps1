[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PackageCertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$PackageCertificatePassword,

    [string]$SetupCertificatePath,
    [string]$SetupCertificatePassword,
    [string]$Publisher = 'CN=MesmerPrism',
    [string]$PackageTimestampUrl = 'http://timestamp.digicert.com',
    [string]$SetupTimestampUrl = 'http://timestamp.digicert.com',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$KioskBundleDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\kiosk-bundle'),

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedKioskVersion,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedKioskSourceRevision,

    [string]$ApkSignerPath,
    [switch]$SkipBuildAndTest
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output must stay under $artifactsRoot."
}
if (-not $SetupCertificatePath) { $SetupCertificatePath = $PackageCertificatePath }
if (-not $SetupCertificatePassword) { $SetupCertificatePassword = $PackageCertificatePassword }

$KioskBundleDirectory = [IO.Path]::GetFullPath($KioskBundleDirectory)
$requiredKioskFiles = @(
    'rusty-kiosk.apk',
    'rusty-kiosk-setup-helper.apk',
    'bundle-manifest.json',
    'RUSTY-KIOSK-LICENSE.txt',
    'RUSTY-KIOSK-SOURCE.txt'
)
foreach ($name in $requiredKioskFiles) {
    $path = Join-Path $KioskBundleDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The public Windows release requires the complete Rusty Kiosk bundle; missing $path"
    }
}
$kioskVerification = & (Join-Path $PSScriptRoot 'Test-RustyKioskReleaseBundle.ps1') `
    -BundleDirectory $KioskBundleDirectory `
    -ExpectedVersion $ExpectedKioskVersion `
    -ExpectedSourceRevision $ExpectedKioskSourceRevision `
    -ApkSignerPath $ApkSignerPath
$defaultKioskBundle = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\kiosk-bundle'))
if (-not $KioskBundleDirectory.Equals($defaultKioskBundle, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $defaultKioskBundle) {
        Remove-Item -LiteralPath $defaultKioskBundle -Recurse -Force
    }
    New-Item -ItemType Directory -Path $defaultKioskBundle -Force | Out-Null
    Copy-Item -Path (Join-Path $KioskBundleDirectory '*') -Destination $defaultKioskBundle -Recurse -Force
    $KioskBundleDirectory = $defaultKioskBundle
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if (-not $SkipBuildAndTest) {
    & dotnet restore (Join-Path $repoRoot 'MetaQuestFileManager.slnx')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build (Join-Path $repoRoot 'MetaQuestFileManager.slnx') --configuration Release --no-restore -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet test (Join-Path $repoRoot 'MetaQuestFileManager.slnx') --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools\Test-PublicBoundary.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Public-boundary validation failed.' }
}

$appPublish = Join-Path $repoRoot 'artifacts\portable-app'
$cliPublish = Join-Path $repoRoot 'artifacts\portable-cli'
$combined = Join-Path $repoRoot 'artifacts\portable-combined'
foreach ($directory in @($appPublish, $cliPublish, $combined)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

& dotnet publish (Join-Path $repoRoot 'src\MetaQuestFileManager.App\MetaQuestFileManager.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version --output $appPublish
if ($LASTEXITCODE -ne 0) { throw 'Portable app publish failed.' }
& dotnet publish (Join-Path $repoRoot 'src\MetaQuestFileManager.Cli\MetaQuestFileManager.Cli.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version --output $cliPublish
if ($LASTEXITCODE -ne 0) { throw 'Portable CLI publish failed.' }

Copy-Item -Path (Join-Path $appPublish '*') -Destination $combined -Recurse -Force
Copy-Item -LiteralPath (Join-Path $cliPublish 'meta-quest-file-manager.exe') -Destination $combined -Force
Compress-Archive -Path (Join-Path $combined '*') -DestinationPath (Join-Path $OutputDirectory 'MetaQuestFileManager-win-x64.zip')
Compress-Archive -Path (Join-Path $cliPublish '*') -DestinationPath (Join-Path $OutputDirectory 'meta-quest-file-manager-cli-win-x64.zip')

& (Join-Path $PSScriptRoot 'Build-App-Package.ps1') `
    -Version $Version `
    -OutputDirectory $OutputDirectory `
    -Publisher $Publisher `
    -CertificatePath $PackageCertificatePath `
    -CertificatePassword $PackageCertificatePassword `
    -TimestampUrl $PackageTimestampUrl
if ($LASTEXITCODE -ne 0) { throw 'MSIX package build failed.' }

& (Join-Path $PSScriptRoot 'Publish-GuidedSetup.ps1') `
    -Version $Version `
    -OutputDirectory $OutputDirectory `
    -CertificatePath $SetupCertificatePath `
    -CertificatePassword $SetupCertificatePassword `
    -TimestampUrl $SetupTimestampUrl
if ($LASTEXITCODE -ne 0) { throw 'Guided setup publish failed.' }

& (Join-Path $PSScriptRoot 'Test-BrandAssets.ps1') -Executable @(
    (Join-Path $appPublish 'MetaQuestFileManager.exe'),
    (Join-Path $cliPublish 'meta-quest-file-manager.exe'),
    (Join-Path $OutputDirectory 'MetaQuestFileManager-Setup.exe')
)
if ($LASTEXITCODE -ne 0) { throw 'Brand asset validation failed.' }

& (Join-Path $PSScriptRoot 'Test-ReleaseAssets.ps1') `
    -ReleaseDirectory $OutputDirectory `
    -ExpectedPublisher $Publisher `
    -KioskBundleManifestPath (Join-Path $KioskBundleDirectory 'bundle-manifest.json') `
    -AllowSelfIssuedTrustFailure
if ($LASTEXITCODE -ne 0) { throw 'Release asset validation failed.' }

Get-ChildItem -LiteralPath $OutputDirectory -File |
    Where-Object Name -ne 'SHA256SUMS.txt' |
    Sort-Object Name |
    ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8

Get-ChildItem -LiteralPath $OutputDirectory -File | Sort-Object Name | Select-Object Name, Length, FullName
