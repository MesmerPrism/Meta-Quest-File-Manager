[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$FileName = 'QuestIonAbleFileManager-Setup.exe',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $repoRoot 'src\QuestIonAbleFileManager.Setup\QuestIonAbleFileManager.Setup.csproj'
$publishDirectory = Join-Path $repoRoot 'artifacts\setup-publish'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $Unsigned) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath) -or -not (Test-Path -LiteralPath $CertificatePath)) {
        throw 'A valid -CertificatePath is required for a signed setup helper.'
    }
    if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        throw '-CertificatePassword is required for a signed setup helper.'
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

& dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed with exit code $LASTEXITCODE" }

$publishedExe = Join-Path $publishDirectory 'QuestIonAbleFileManager.Setup.exe'
$outputExe = Join-Path $OutputDirectory $FileName
Copy-Item -LiteralPath $publishedExe -Destination $outputExe -Force

if (-not $Unsigned) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $sdkBin -Directory |
        Where-Object Name -Match '^\d+\.\d+\.\d+\.\d+$' |
        Sort-Object { [Version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $signTool) { throw 'signtool.exe was not found in the Windows SDK.' }

    & $signTool sign /fd SHA256 /f ([IO.Path]::GetFullPath($CertificatePath)) /p $CertificatePassword /tr $TimestampUrl /td SHA256 $outputExe
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $FileName with exit code $LASTEXITCODE" }
}

Get-Item -LiteralPath $outputExe | Select-Object Name, Length, FullName
