[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)

Add-Type -AssemblyName System.Drawing.Common

$brandingDirectory = Join-Path $RepositoryRoot 'assets\branding'
$packageDirectory = Join-Path $RepositoryRoot 'src\MetaQuestFileManager.App.Package\Images'
$siteDirectory = Join-Path $RepositoryRoot 'site'

foreach ($directory in @($brandingDirectory, $packageDirectory, $siteDirectory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$backgroundColor = [Drawing.Color]::FromArgb(255, 243, 241, 236)
$backColor = [Drawing.Color]::FromArgb(255, 106, 91, 66)
$frontColor = [Drawing.Color]::FromArgb(255, 201, 148, 82)

function New-FolderBitmap {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Size,

        [switch]$WithBackground
    )

    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear($(if ($WithBackground) { $backgroundColor } else { [Drawing.Color]::Transparent }))

        $scale = $Size / 512.0
        $points = {
            param([float[][]]$Coordinates)
            [Drawing.PointF[]]@($Coordinates | ForEach-Object {
                [Drawing.PointF]::new($_[0] * $scale, $_[1] * $scale)
            })
        }

        $backBrush = [Drawing.SolidBrush]::new($backColor)
        $frontBrush = [Drawing.SolidBrush]::new($frontColor)
        try {
            $back = & $points @(
                @(64.0, 112.0),
                @(212.0, 112.0),
                @(260.0, 160.0),
                @(448.0, 160.0),
                @(448.0, 232.0),
                @(64.0, 232.0)
            )
            $front = & $points @(
                @(52.0, 192.0),
                @(460.0, 192.0),
                @(428.0, 420.0),
                @(84.0, 420.0)
            )

            $graphics.FillPolygon($backBrush, $back)
            $graphics.FillPolygon($frontBrush, $front)
        }
        finally {
            $backBrush.Dispose()
            $frontBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Save-FolderPng {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Size,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [switch]$WithBackground
    )

    $bitmap = New-FolderBitmap -Size $Size -WithBackground:$WithBackground
    try {
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-FolderPngBytes {
    param([Parameter(Mandatory = $true)][int]$Size)

    $bitmap = New-FolderBitmap -Size $Size
    $stream = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

function Save-FolderIco {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $images = [Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $images.Add((Get-FolderPngBytes -Size $size))
    }
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $encodedSize = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$images[$index].Length)
            $writer.Write([UInt32]$offset)
            $offset += $images[$index].Length
        }

        foreach ($image in $images) {
            $writer.Write([byte[]]$image)
        }

        $writer.Flush()
        [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="Folder">
  <path fill="#6A5B42" d="M64 112h148l48 48h188v72H64z"/>
  <path fill="#C99452" d="M52 192h408l-32 228H84z"/>
</svg>
'@

$svgPath = Join-Path $brandingDirectory 'folder-icon.svg'
[IO.File]::WriteAllText($svgPath, $svg, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $siteDirectory 'folder-icon.svg'), $svg, [Text.UTF8Encoding]::new($false))

Save-FolderPng -Size 512 -Path (Join-Path $brandingDirectory 'folder-icon-512.png')
Save-FolderIco -Path (Join-Path $brandingDirectory 'MetaQuestFileManager.ico')

Save-FolderPng -Size 44 -Path (Join-Path $packageDirectory 'Square44x44Logo.png')
Save-FolderPng -Size 150 -Path (Join-Path $packageDirectory 'Square150x150Logo.png')
Save-FolderPng -Size 50 -Path (Join-Path $packageDirectory 'StoreLogo.png')

Save-FolderIco -Path (Join-Path $siteDirectory 'favicon.ico')
Save-FolderPng -Size 192 -Path (Join-Path $siteDirectory 'icon-192.png')
Save-FolderPng -Size 512 -Path (Join-Path $siteDirectory 'icon-512.png')
Save-FolderPng -Size 180 -Path (Join-Path $siteDirectory 'apple-touch-icon.png') -WithBackground

Get-ChildItem -LiteralPath $brandingDirectory, $packageDirectory, $siteDirectory -File |
    Where-Object { $_.Name -match '^(folder-icon|MetaQuestFileManager|Square|StoreLogo|favicon|icon-|apple-touch)' } |
    Sort-Object FullName |
    Select-Object FullName, Length
