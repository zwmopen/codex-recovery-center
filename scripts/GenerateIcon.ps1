param([Parameter(Mandatory = $true)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )
    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [float]($size * 0.06)
    $radius = [float]($size * 0.23)
    $bounds = New-Object System.Drawing.RectangleF $margin, $margin,
        ([float]($size - 2 * $margin)), ([float]($size - 2 * $margin))
    $backgroundPath = New-RoundedPath $bounds $radius
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $bounds, ([System.Drawing.Color]::FromArgb(66, 145, 255)),
        ([System.Drawing.Color]::FromArgb(31, 94, 220)), 55
    $graphics.FillPath($background, $backgroundPath)

    $lineWidth = [Math]::Max(1.5, $size * 0.075)
    $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(246, 255, 255, 255)),
        ([float]$lineWidth)
    $line.StartCap = $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arcBounds = New-Object System.Drawing.RectangleF `
        ([float]($size * 0.25)), ([float]($size * 0.25)),
        ([float]($size * 0.50)), ([float]($size * 0.50))
    $graphics.DrawArc($line, $arcBounds, -55, 280)

    $arrow = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([float]($size * 0.25)), ([float]($size * 0.28))),
        (New-Object System.Drawing.PointF ([float]($size * 0.45)), ([float]($size * 0.29))),
        (New-Object System.Drawing.PointF ([float]($size * 0.31)), ([float]($size * 0.44)))
    )
    $arrowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $graphics.FillPolygon($arrowBrush, $arrow)

    $centerBrush = New-Object System.Drawing.SolidBrush `
        ([System.Drawing.Color]::FromArgb(210, 233, 255))
    $centerSize = [float]($size * 0.13)
    $graphics.FillEllipse($centerBrush,
        ([float](($size - $centerSize) / 2)), ([float](($size - $centerSize) / 2)),
        $centerSize, $centerSize)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{
        Size = $size
        Bytes = $stream.ToArray()
    }

    $stream.Dispose()
    $centerBrush.Dispose()
    $arrowBrush.Dispose()
    $line.Dispose()
    $background.Dispose()
    $backgroundPath.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$file = [System.IO.File]::Open(
    $OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + (16 * $images.Count)

    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
