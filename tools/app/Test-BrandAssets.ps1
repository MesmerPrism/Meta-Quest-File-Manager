[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..\..'),
    [string[]]$Executable = @()
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
Add-Type -AssemblyName System.Drawing.Common

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Png {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$Size,
        [Parameter(Mandatory = $true)][bool]$TransparentCorner
    )

    Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) "Missing PNG: $Path"
    $bitmap = [Drawing.Bitmap]::FromFile($Path)
    try {
        Assert-Condition ($bitmap.Width -eq $Size -and $bitmap.Height -eq $Size) "Unexpected PNG dimensions: $Path"
        $corner = $bitmap.GetPixel(0, 0)
        if ($TransparentCorner) {
            Assert-Condition ($corner.A -eq 0) "Expected a transparent PNG corner: $Path"
        }
        else {
            Assert-Condition ($corner.A -eq 255) "Expected an opaque PNG corner: $Path"
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Assert-Ico {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) "Missing ICO: $Path"
    $expectedSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        Assert-Condition ($reader.ReadUInt16() -eq 0) "Invalid ICO reserved field: $Path"
        Assert-Condition ($reader.ReadUInt16() -eq 1) "Invalid ICO image type: $Path"
        $count = $reader.ReadUInt16()
        Assert-Condition ($count -eq $expectedSizes.Count) "Expected $($expectedSizes.Count) ICO frames, found $count in $Path"

        $entries = @()
        for ($index = 0; $index -lt $count; $index++) {
            $width = $reader.ReadByte()
            $height = $reader.ReadByte()
            [void]$reader.ReadByte()
            [void]$reader.ReadByte()
            [void]$reader.ReadUInt16()
            [void]$reader.ReadUInt16()
            $length = $reader.ReadUInt32()
            $offset = $reader.ReadUInt32()
            $entries += [pscustomobject]@{
                Width = if ($width -eq 0) { 256 } else { [int]$width }
                Height = if ($height -eq 0) { 256 } else { [int]$height }
                Length = $length
                Offset = $offset
            }
        }

        Assert-Condition (-not (Compare-Object $expectedSizes @($entries.Width))) "Unexpected ICO frame sizes in $Path"
        foreach ($entry in $entries) {
            Assert-Condition ($entry.Width -eq $entry.Height) "ICO frame is not square in $Path"
            $stream.Position = $entry.Offset
            $signature = $reader.ReadBytes(8)
            $pngSignature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
            Assert-Condition (($signature -join ',') -eq ($pngSignature -join ',')) "ICO frame is not PNG encoded in $Path"
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

$brandingDirectory = Join-Path $RepositoryRoot 'assets\branding'
$packageDirectory = Join-Path $RepositoryRoot 'src\MetaQuestFileManager.App.Package\Images'
$siteDirectory = Join-Path $RepositoryRoot 'site'
$applicationIcon = Join-Path $brandingDirectory 'MetaQuestFileManager.ico'

Assert-Ico $applicationIcon
Assert-Ico (Join-Path $siteDirectory 'favicon.ico')
Assert-Png (Join-Path $brandingDirectory 'folder-icon-512.png') 512 $true
Assert-Png (Join-Path $packageDirectory 'Square44x44Logo.png') 44 $true
Assert-Png (Join-Path $packageDirectory 'Square150x150Logo.png') 150 $true
Assert-Png (Join-Path $packageDirectory 'StoreLogo.png') 50 $true
Assert-Png (Join-Path $siteDirectory 'icon-192.png') 192 $true
Assert-Png (Join-Path $siteDirectory 'icon-512.png') 512 $true
Assert-Png (Join-Path $siteDirectory 'apple-touch-icon.png') 180 $false

$brandingSvg = Get-Content -LiteralPath (Join-Path $brandingDirectory 'folder-icon.svg') -Raw
$siteSvg = Get-Content -LiteralPath (Join-Path $siteDirectory 'folder-icon.svg') -Raw
Assert-Condition ($brandingSvg -eq $siteSvg) 'The website SVG differs from the canonical branding SVG.'

foreach ($projectPath in @(
    'src\MetaQuestFileManager.App\MetaQuestFileManager.App.csproj',
    'src\MetaQuestFileManager.Cli\MetaQuestFileManager.Cli.csproj',
    'src\MetaQuestFileManager.Setup\MetaQuestFileManager.Setup.csproj'
)) {
    $fullProjectPath = Join-Path $RepositoryRoot $projectPath
    [xml]$project = Get-Content -LiteralPath $fullProjectPath -Raw
    $iconNode = $project.Project.PropertyGroup.ApplicationIcon | Select-Object -First 1
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($iconNode)) "ApplicationIcon is missing from $projectPath"
    $resolvedIcon = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $fullProjectPath) $iconNode))
    Assert-Condition ($resolvedIcon -eq $applicationIcon) "ApplicationIcon does not resolve to the canonical icon in $projectPath"
}

foreach ($executablePath in $Executable) {
    $resolvedExecutable = [IO.Path]::GetFullPath($executablePath)
    Assert-Condition (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf) "Missing executable: $resolvedExecutable"
    $icon = [Drawing.Icon]::ExtractAssociatedIcon($resolvedExecutable)
    Assert-Condition ($null -ne $icon) "No associated icon was found in $resolvedExecutable"
    try {
        $bitmap = $icon.ToBitmap()
        try {
            $center = $bitmap.GetPixel([Math]::Floor($bitmap.Width / 2), [Math]::Floor($bitmap.Height / 2))
            $distance = [Math]::Abs($center.R - 201) + [Math]::Abs($center.G - 148) + [Math]::Abs($center.B - 82)
            Assert-Condition ($distance -le 24) "The embedded executable icon does not contain the expected folder color: $resolvedExecutable"
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $icon.Dispose()
    }
}

[pscustomobject]@{
    BrandingAssets = 5
    PackageAssets = 3
    SiteAssets = 6
    ExecutablesChecked = $Executable.Count
    Status = 'passed'
}
