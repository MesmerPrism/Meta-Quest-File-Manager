[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\site\assets\onboarding')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'OnboardingPreview\QuestIonAbleFileManager.OnboardingPreview.csproj'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet run --project $project --configuration Release -- $output
if ($LASTEXITCODE -ne 0) {
    throw "The onboarding preview renderer failed with exit code $LASTEXITCODE."
}

$expected = @(
    'file-manager-files.png',
    'file-manager-kiosk-setup.png',
    'file-manager-kiosk-apps.png'
)
foreach ($name in $expected) {
    $path = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing onboarding preview: $path"
    }
}

Get-Item ($expected | ForEach-Object { Join-Path $output $_ }) |
    Select-Object Name, Length, FullName
