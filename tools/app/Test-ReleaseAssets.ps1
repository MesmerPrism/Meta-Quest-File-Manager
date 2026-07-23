[CmdletBinding()]
param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$ExpectedPackageName = 'MesmerPrism.MetaQuestFileManager',
    [string]$ExpectedPublisher = 'CN=MesmerPrism',
    [string]$KioskBundleManifestPath,
    [switch]$AllowSelfIssuedTrustFailure
)

$ErrorActionPreference = 'Stop'
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$setupPath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager-Setup.exe'
$packagePath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager-win-x64.msix'
$appInstallerPath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager.appinstaller'
$certificatePath = Join-Path $ReleaseDirectory 'QuestIonAbleFileManager.cer'
$receiptPath = Join-Path $ReleaseDirectory 'release-validation.json'
$legacyAliases = [ordered]@{
    'MetaQuestFileManager-Setup.exe' = 'QuestIonAbleFileManager-Setup.exe'
    'MetaQuestFileManager-win-x64.msix' = 'QuestIonAbleFileManager-win-x64.msix'
    'MetaQuestFileManager.appinstaller' = 'QuestIonAbleFileManager.appinstaller'
    'MetaQuestFileManager.cer' = 'QuestIonAbleFileManager.cer'
    'MetaQuestFileManager-win-x64.zip' = 'QuestIonAbleFileManager-win-x64.zip'
    'meta-quest-file-manager-cli-win-x64.zip' = 'questionable-file-manager-cli-win-x64.zip'
}

foreach ($path in @($setupPath, $packagePath, $appInstallerPath, $certificatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release asset was not found: $path"
    }
}
foreach ($entry in $legacyAliases.GetEnumerator()) {
    $legacyPath = Join-Path $ReleaseDirectory $entry.Key
    $canonicalPath = Join-Path $ReleaseDirectory $entry.Value
    if (-not (Test-Path -LiteralPath $legacyPath -PathType Leaf)) {
        throw "Required compatibility alias was not found: $legacyPath"
    }
    if ((Get-FileHash -LiteralPath $legacyPath -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $canonicalPath -Algorithm SHA256).Hash) {
        throw "Compatibility alias differs from its canonical asset: $($entry.Key)"
    }
}

function Test-Signature {
    param([Parameter(Mandatory = $true)][string]$Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($null -eq $signature.SignerCertificate) {
        throw "No Authenticode signer was found on $Path."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "No RFC 3161 timestamp was found on $Path."
    }
    if ($signature.Status -ne 'Valid') {
        $allowed = $AllowSelfIssuedTrustFailure -and $signature.Status -eq 'UnknownError'
        if (-not $allowed) {
            throw "Signature validation failed for $Path with status $($signature.Status): $($signature.StatusMessage)"
        }
    }

    return [pscustomobject]@{
        Path = [IO.Path]::GetFileName($Path)
        Status = [string]$signature.Status
        SignerSubject = $signature.SignerCertificate.Subject
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
        TimestampSubject = $signature.TimeStamperCertificate.Subject
        TimestampNotBefore = $signature.TimeStamperCertificate.NotBefore.ToUniversalTime().ToString('o')
        TimestampNotAfter = $signature.TimeStamperCertificate.NotAfter.ToUniversalTime().ToString('o')
    }
}

$setupSignature = Test-Signature -Path $setupPath
$packageSignature = Test-Signature -Path $packagePath
if ($setupSignature.SignerSubject -ne $ExpectedPublisher) {
    throw "The setup helper signer was '$($setupSignature.SignerSubject)', expected '$ExpectedPublisher'."
}
if ($packageSignature.SignerSubject -ne $ExpectedPublisher) {
    throw "The MSIX signer was '$($packageSignature.SignerSubject)', expected '$ExpectedPublisher'."
}

$certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile($certificatePath)
try {
    if ($certificate.Thumbprint -ne $packageSignature.SignerThumbprint) {
        throw 'The public CER does not match the package signer.'
    }
    if ($certificate.Subject -ne $ExpectedPublisher) {
        throw "The public CER publisher was '$($certificate.Subject)', expected '$ExpectedPublisher'."
    }
}
finally {
    $certificate.Dispose()
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @('AppxManifest.xml', 'AppxBlockMap.xml', 'AppxSignature.p7x')) {
        if ($entries -notcontains $required) {
            throw "The MSIX package is missing $required."
        }
    }
    if (-not ($entries | Where-Object { $_ -match '(^|/)QuestIonAbleFileManager\.exe$' })) {
        throw 'The MSIX package does not contain QuestIonAbleFileManager.exe.'
    }
}
finally {
    $archive.Dispose()
}

[xml]$appInstaller = Get-Content -LiteralPath $appInstallerPath -Raw
$namespace = [Xml.XmlNamespaceManager]::new($appInstaller.NameTable)
$namespace.AddNamespace('ai', $appInstaller.DocumentElement.NamespaceURI)
$mainPackage = $appInstaller.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $namespace)
if ($null -eq $mainPackage) { throw 'The App Installer feed is missing MainPackage.' }
if ($mainPackage.Name -ne $ExpectedPackageName) { throw "Unexpected App Installer package name: $($mainPackage.Name)" }
if ($mainPackage.Publisher -ne $ExpectedPublisher) { throw "Unexpected App Installer publisher: $($mainPackage.Publisher)" }
if ($mainPackage.Uri -notmatch '^https://github\.com/MesmerPrism/QuestIonAble-File-Manager/releases/latest/download/') {
    throw "The published App Installer MSIX URI is not release-stable: $($mainPackage.Uri)"
}

$kioskReceipt = $null
if ($KioskBundleManifestPath) {
    $KioskBundleManifestPath = [IO.Path]::GetFullPath($KioskBundleManifestPath)
    if (-not (Test-Path -LiteralPath $KioskBundleManifestPath -PathType Leaf)) {
        throw "The verified Rusty Kiosk manifest was not found: $KioskBundleManifestPath"
    }
    $kioskManifest = Get-Content -Raw -LiteralPath $KioskBundleManifestPath | ConvertFrom-Json
    $kioskReceipt = [ordered]@{
        version = $kioskManifest.version
        source_url = $kioskManifest.source_url
        source_revision = $kioskManifest.source_revision
        signer_sha256 = $kioskManifest.signer_sha256
        bundle_manifest_sha256 = (Get-FileHash -LiteralPath $KioskBundleManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        files = $kioskManifest.files
    }
}

$receipt = [ordered]@{
    schema = 'questionable-file-manager.release-validation.v1'
    validated_at_utc = [DateTime]::UtcNow.ToString('o')
    package_name = $ExpectedPackageName
    package_version = $mainPackage.Version
    publisher = $ExpectedPublisher
    setup_signature = $setupSignature
    package_signature = $packageSignature
    appinstaller_uri = $appInstaller.AppInstaller.Uri
    msix_uri = $mainPackage.Uri
    rusty_kiosk = $kioskReceipt
    required_assets = @(
        'QuestIonAbleFileManager-Setup.exe',
        'QuestIonAbleFileManager-win-x64.msix',
        'QuestIonAbleFileManager.appinstaller',
        'QuestIonAbleFileManager.cer'
    )
    compatibility_aliases = $legacyAliases
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
$receipt
