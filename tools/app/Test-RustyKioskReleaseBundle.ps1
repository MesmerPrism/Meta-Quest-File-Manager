[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')]
    [string]$BundleDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSourceRevision,

    [Parameter(ParameterSetName = 'Verify')]
    [string]$ApkSignerPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'SelfTest')]
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

if ($SelfTest) {
    $artifactsRoot = Join-Path $repoRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
    $testRoot = Join-Path $artifactsRoot ("kiosk-bundle-verifier-{0}" -f [guid]::NewGuid().ToString('N'))
    $bundle = Join-Path $testRoot 'bundle'
    $fakeSigner = Join-Path $testRoot 'apksigner.cmd'
    $version = '9.8.7'
    $revision = '0123456789abcdef0123456789abcdef01234567'
    $signer = 'a' * 64

    try {
        New-Item -ItemType Directory -Path $bundle -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $bundle 'rusty-kiosk.apk'), [byte[]](1, 2, 3, 4))
        [IO.File]::WriteAllBytes((Join-Path $bundle 'rusty-kiosk-setup-helper.apk'), [byte[]](5, 6, 7))
        Set-Content -LiteralPath (Join-Path $bundle 'RUSTY-KIOSK-LICENSE.txt') -Encoding utf8 -Value 'test license'
        Set-Content -LiteralPath (Join-Path $bundle 'RUSTY-KIOSK-SOURCE.txt') -Encoding utf8 -Value @"
Rusty Kiosk source: https://github.com/MesmerPrism/Rusty-Kiosk
Source revision: $revision
Version: $version
License: GNU Affero General Public License v3.0 or later (see RUSTY-KIOSK-LICENSE.txt)
"@
        Set-Content -LiteralPath $fakeSigner -Encoding ascii -Value @"
@echo Signer #1 certificate SHA-256 digest: $signer 1>&2
"@

        $fileNames = @(
            'rusty-kiosk.apk',
            'rusty-kiosk-setup-helper.apk',
            'RUSTY-KIOSK-LICENSE.txt',
            'RUSTY-KIOSK-SOURCE.txt'
        )
        $manifest = [ordered]@{
            schema = 'meta.quest.file_manager.rusty_kiosk_bundle.v1'
            build_type = 'release'
            version = $version
            source_url = 'https://github.com/MesmerPrism/Rusty-Kiosk'
            source_revision = $revision
            signer_sha256 = $signer
            staged_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
            files = @($fileNames | ForEach-Object {
                $path = Join-Path $bundle $_
                [ordered]@{
                    name = $_
                    sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
                    bytes = (Get-Item -LiteralPath $path).Length
                }
            })
        }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $bundle 'bundle-manifest.json') -Encoding utf8

        $null = & $PSCommandPath `
            -BundleDirectory $bundle `
            -ExpectedVersion $version `
            -ExpectedSourceRevision $revision `
            -ApkSignerPath $fakeSigner

        Add-Content -LiteralPath (Join-Path $bundle 'rusty-kiosk.apk') -Value 'tamper'
        $tamperRejected = $false
        try {
            $null = & $PSCommandPath `
                -BundleDirectory $bundle `
                -ExpectedVersion $version `
                -ExpectedSourceRevision $revision `
                -ApkSignerPath $fakeSigner
        }
        catch {
            $tamperRejected = $true
        }
        if (-not $tamperRejected) {
            throw 'The Rusty Kiosk bundle verifier accepted a tampered APK.'
        }

        Write-Output 'Rusty Kiosk release-bundle verifier self-test passed.'
        return
    }
    finally {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$BundleDirectory = [IO.Path]::GetFullPath($BundleDirectory)
$manifestPath = Join-Path $BundleDirectory 'bundle-manifest.json'
$expectedSourceUrl = 'https://github.com/MesmerPrism/Rusty-Kiosk'
$expectedFiles = @(
    'rusty-kiosk.apk',
    'rusty-kiosk-setup-helper.apk',
    'RUSTY-KIOSK-LICENSE.txt',
    'RUSTY-KIOSK-SOURCE.txt'
)
foreach ($name in @($expectedFiles + 'bundle-manifest.json')) {
    $path = Join-Path $BundleDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The Rusty Kiosk release bundle is incomplete; missing $name."
    }
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schema -ne 'meta.quest.file_manager.rusty_kiosk_bundle.v1' -or $manifest.build_type -ne 'release') {
    throw 'The Rusty Kiosk bundle does not use the supported release manifest.'
}
if ($manifest.version -ne $ExpectedVersion) {
    throw "Rusty Kiosk bundle version '$($manifest.version)' does not match requested release '$ExpectedVersion'."
}
if ($manifest.source_url -ne $expectedSourceUrl) {
    throw "Unexpected Rusty Kiosk source URL: $($manifest.source_url)"
}
if ($manifest.source_revision -ine $ExpectedSourceRevision) {
    throw "Rusty Kiosk source revision '$($manifest.source_revision)' does not match tag commit '$ExpectedSourceRevision'."
}
if ([string]$manifest.signer_sha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw 'The Rusty Kiosk manifest signer digest is missing or malformed.'
}

$manifestFiles = @($manifest.files)
if ($manifestFiles.Count -ne $expectedFiles.Count) {
    throw "The Rusty Kiosk manifest must describe exactly $($expectedFiles.Count) payload files."
}
$manifestNames = @($manifestFiles | ForEach-Object { [string]$_.name })
if (@($manifestNames | Select-Object -Unique).Count -ne $manifestNames.Count -or
    @(Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $manifestNames).Count -ne 0) {
    throw 'The Rusty Kiosk manifest payload names are incomplete, duplicated, or unexpected.'
}
foreach ($file in $manifestFiles) {
    $path = Join-Path $BundleDirectory $file.name
    if ([string]$file.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or [long]$file.bytes -lt 1) {
        throw "Rusty Kiosk manifest metadata is malformed for $($file.name)."
    }
    $actualBytes = (Get-Item -LiteralPath $path).Length
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualBytes -ne [long]$file.bytes -or $actualHash -ine [string]$file.sha256) {
        throw "Rusty Kiosk payload does not match its manifest: $($file.name)."
    }
}

$sourceLines = @(Get-Content -LiteralPath (Join-Path $BundleDirectory 'RUSTY-KIOSK-SOURCE.txt'))
foreach ($requiredLine in @(
    "Rusty Kiosk source: $expectedSourceUrl",
    "Source revision: $ExpectedSourceRevision",
    "Version: $ExpectedVersion"
)) {
    if ($sourceLines -notcontains $requiredLine) {
        throw "Rusty Kiosk source pointer is inconsistent; missing '$requiredLine'."
    }
}

if (-not $ApkSignerPath) {
    $sdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $ApkSignerPath = @($sdkRoots | ForEach-Object {
        Get-ChildItem -Path (Join-Path $_ 'build-tools\*\apksigner.bat') -ErrorAction SilentlyContinue
    } | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $ApkSignerPath -or -not (Test-Path -LiteralPath $ApkSignerPath -PathType Leaf)) {
    throw 'apksigner is required to verify the Rusty Kiosk release bundle.'
}

function Get-ApkSignerDigest {
    param([Parameter(Mandatory = $true)][string]$Path)
    $output = & $ApkSignerPath verify --print-certs $Path 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "APK signature verification failed for $([IO.Path]::GetFileName($Path))."
    }
    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $match = [regex]::Match(
        $text,
        '(?im)Signer\s+#?1\s+certificate\s+SHA-?256\s+digest\s*:\s*([0-9a-fA-F:\- ]{64,128})'
    )
    if (-not $match.Success) {
        throw "No APK signer digest was reported for $([IO.Path]::GetFileName($Path)).`nVerifier output:`n$text"
    }
    $digest = ($match.Groups[1].Value -replace '[^0-9a-fA-F]', '').ToLowerInvariant()
    if ($digest.Length -ne 64) {
        throw "The APK signer digest for $([IO.Path]::GetFileName($Path)) was not a 32-byte SHA-256 value: $digest"
    }
    return $digest
}

$mainSigner = Get-ApkSignerDigest -Path (Join-Path $BundleDirectory 'rusty-kiosk.apk')
$helperSigner = Get-ApkSignerDigest -Path (Join-Path $BundleDirectory 'rusty-kiosk-setup-helper.apk')
$expectedSigner = ([string]$manifest.signer_sha256).ToLowerInvariant()
if ($mainSigner -ne $helperSigner -or $mainSigner -ne $expectedSigner) {
    throw 'The Kiosk APK pair does not match the same signer recorded in its release manifest.'
}

[pscustomobject]@{
    version = $manifest.version
    source_url = $manifest.source_url
    source_revision = ([string]$manifest.source_revision).ToLowerInvariant()
    signer_sha256 = $expectedSigner
    manifest_sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    files = $manifestFiles
}
