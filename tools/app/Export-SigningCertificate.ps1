[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$Thumbprint,

    [Parameter(Mandatory = $true)]
    [Security.SecureString]$Password,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\signing')
)

$ErrorActionPreference = 'Stop'
$certificatePath = "Cert:\CurrentUser\My\$($Thumbprint.ToUpperInvariant())"
$certificate = Get-Item -LiteralPath $certificatePath -ErrorAction Stop
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate does not have an accessible private key.' }

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$pfxPath = Join-Path $OutputDirectory 'windows-signing.pfx'
$cerPath = Join-Path $OutputDirectory 'windows-signing.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $Password -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT -Force | Out-Null

[pscustomobject]@{
    Subject = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    NotAfter = $certificate.NotAfter.ToUniversalTime().ToString('o')
    PfxPath = $pfxPath
    CerPath = $cerPath
}
