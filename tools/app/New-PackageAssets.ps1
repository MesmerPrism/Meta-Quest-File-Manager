[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\src\MetaQuestFileManager.App.Package\Images')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing.Common
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function New-PackageGlyph {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Size,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(255, 243, 241, 236))

        $scale = $Size / 150.0
        $folder = [Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $folder.AddPolygon([Drawing.PointF[]]@(
                [Drawing.PointF]::new(22 * $scale, 49 * $scale),
                [Drawing.PointF]::new(56 * $scale, 49 * $scale),
                [Drawing.PointF]::new(67 * $scale, 38 * $scale),
                [Drawing.PointF]::new(128 * $scale, 38 * $scale),
                [Drawing.PointF]::new(128 * $scale, 112 * $scale),
                [Drawing.PointF]::new(22 * $scale, 112 * $scale)
            ))

            $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 106, 91, 66))
            $stroke = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 37, 38, 34), [Math]::Max(1.0, 4 * $scale))
            try {
                $graphics.FillPath($fill, $folder)
                $graphics.DrawPath($stroke, $folder)
            }
            finally {
                $fill.Dispose()
                $stroke.Dispose()
            }
        }
        finally {
            $folder.Dispose()
        }

        $lensFill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 255, 255, 255))
        $lensStroke = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 37, 38, 34), [Math]::Max(1.0, 4 * $scale))
        try {
            $graphics.FillEllipse($lensFill, 39 * $scale, 65 * $scale, 30 * $scale, 25 * $scale)
            $graphics.FillEllipse($lensFill, 81 * $scale, 65 * $scale, 30 * $scale, 25 * $scale)
            $graphics.DrawEllipse($lensStroke, 39 * $scale, 65 * $scale, 30 * $scale, 25 * $scale)
            $graphics.DrawEllipse($lensStroke, 81 * $scale, 65 * $scale, 30 * $scale, 25 * $scale)
            $graphics.DrawLine($lensStroke, 68 * $scale, 77 * $scale, 82 * $scale, 77 * $scale)
        }
        finally {
            $lensFill.Dispose()
            $lensStroke.Dispose()
        }

        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-PackageGlyph -Size 44 -Path (Join-Path $OutputDirectory 'Square44x44Logo.png')
New-PackageGlyph -Size 150 -Path (Join-Path $OutputDirectory 'Square150x150Logo.png')
New-PackageGlyph -Size 50 -Path (Join-Path $OutputDirectory 'StoreLogo.png')

Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length, FullName
