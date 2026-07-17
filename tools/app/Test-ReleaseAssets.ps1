[CmdletBinding()]
param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$ExpectedPackageName = 'MesmerPrism.MetaQuestFileManager',
    [string]$ExpectedPublisher = 'CN=MesmerPrism',
    [switch]$AllowSelfIssuedTrustFailure
)

$ErrorActionPreference = 'Stop'
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$setupPath = Join-Path $ReleaseDirectory 'MetaQuestFileManager-Setup.exe'
$packagePath = Join-Path $ReleaseDirectory 'MetaQuestFileManager-win-x64.msix'
$appInstallerPath = Join-Path $ReleaseDirectory 'MetaQuestFileManager.appinstaller'
$certificatePath = Join-Path $ReleaseDirectory 'MetaQuestFileManager.cer'
$receiptPath = Join-Path $ReleaseDirectory 'release-validation.json'

foreach ($path in @($setupPath, $packagePath, $appInstallerPath, $certificatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release asset was not found: $path"
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
    if (-not ($entries | Where-Object { $_ -match '(^|/)MetaQuestFileManager\.exe$' })) {
        throw 'The MSIX package does not contain MetaQuestFileManager.exe.'
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
if ($mainPackage.Uri -notmatch '^https://github\.com/MesmerPrism/Meta-Quest-File-Manager/releases/latest/download/') {
    throw "The published App Installer MSIX URI is not release-stable: $($mainPackage.Uri)"
}

$receipt = [ordered]@{
    schema = 'meta-quest-file-manager.release-validation.v1'
    validated_at_utc = [DateTime]::UtcNow.ToString('o')
    package_name = $ExpectedPackageName
    package_version = $mainPackage.Version
    publisher = $ExpectedPublisher
    setup_signature = $setupSignature
    package_signature = $packageSignature
    appinstaller_uri = $appInstaller.AppInstaller.Uri
    msix_uri = $mainPackage.Uri
    required_assets = @(
        'MetaQuestFileManager-Setup.exe',
        'MetaQuestFileManager-win-x64.msix',
        'MetaQuestFileManager.appinstaller',
        'MetaQuestFileManager.cer'
    )
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
$receipt
