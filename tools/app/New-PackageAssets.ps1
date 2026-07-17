[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Write-Warning 'New-PackageAssets.ps1 now forwards to New-BrandAssets.ps1 so every application surface stays in sync.'
& (Join-Path $PSScriptRoot 'New-BrandAssets.ps1')
