# Redraws the Almatter logo and writes app/Almatter.App/Assets/almatter-logo.{png,ico}.
#
# Every size in the .ico is drawn on its own (supersampled, then filtered down),
# so Windows never has to shrink a single large frame itself — that shrinking is
# what gave the desktop shortcut its stair-stepped corners.
#
# Usage (Windows PowerShell 5.1):  powershell -File tools\generate-app-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Same dark blue as the official Mattermost desktop app icon.
$background = [System.Drawing.Color]::FromArgb(255, 0x28, 0x42, 0x7B)
$foreground = [System.Drawing.Color]::White

# Geometry on a 256-unit canvas.
$squareInset = 2
$cornerRadius = 28
$strokeWidth = 31
$strokeAngle = 52          # degrees, top-left to bottom-right
# Centre and total length (round caps included) of each stripe.
$stripes = @(
    @{ X = 66.9;  Y = 175.1; Length = 87 },
    @{ X = 107.9; Y = 143.1; Length = 129 },
    @{ X = 153.6; Y = 107.3; Length = 171 }
)

$icoSizes = 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256
$supersample = 8

$assets = Join-Path $PSScriptRoot '..\app\Almatter.App\Assets'

function New-LogoBitmap([int] $size) {
    $big = $size * $supersample
    $scale = $big / 256.0

    $hi = New-Object System.Drawing.Bitmap $big, $big, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [System.Drawing.Graphics]::FromImage($hi)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $x = $squareInset * $scale
    $w = (256 - 2 * $squareInset) * $scale
    $d = 2 * $cornerRadius * $scale
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $x, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $x, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $x + $w - $d, $d, $d, 0, 90)
    $path.AddArc($x, $x + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush $background
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen $foreground, ($strokeWidth * $scale)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rad = $strokeAngle * [math]::PI / 180
    foreach ($s in $stripes) {
        $half = ($s.Length - $strokeWidth) / 2
        $dx = [math]::Cos($rad) * $half
        $dy = [math]::Sin($rad) * $half
        $g.DrawLine($pen,
            [single](($s.X - $dx) * $scale), [single](($s.Y - $dy) * $scale),
            [single](($s.X + $dx) * $scale), [single](($s.Y + $dy) * $scale))
    }
    $g.Dispose(); $pen.Dispose(); $brush.Dispose(); $path.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $attrs = New-Object System.Drawing.Imaging.ImageAttributes
    $attrs.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $g.DrawImage($hi, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), 0, 0, $big, $big,
        [System.Drawing.GraphicsUnit]::Pixel, $attrs)
    $g.Dispose(); $attrs.Dispose(); $hi.Dispose()
    return $out
}

function Get-PngBytes([System.Drawing.Bitmap] $bitmap) {
    $ms = New-Object System.IO.MemoryStream
    $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Classic 32-bit DIB frame (BITMAPINFOHEADER, bottom-up BGRA, then the AND mask),
# which every icon reader understands.
function Get-DibBytes([System.Drawing.Bitmap] $bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)

    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32] 40); $w.Write([Int32] $size); $w.Write([Int32] ($size * 2))
    $w.Write([UInt16] 1); $w.Write([UInt16] 32); $w.Write([UInt32] 0)
    $w.Write([UInt32] ($pixels.Length + $maskStride * $size))
    $w.Write([Int32] 0); $w.Write([Int32] 0); $w.Write([UInt32] 0); $w.Write([UInt32] 0)
    for ($row = $size - 1; $row -ge 0; $row--) { $w.Write($pixels, $row * $size * 4, $size * 4) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

# The 256 px frame is stored as PNG (the usual convention, keeps the file small).
$frames = foreach ($size in $icoSizes) {
    $bmp = New-LogoBitmap $size
    $bytes = if ($size -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    , @($size, $bytes)
    if ($size -eq 256) { $bmp.Save((Join-Path $assets 'almatter-logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([UInt16] 0)              # reserved
$writer.Write([UInt16] 1)              # type: icon
$writer.Write([UInt16] $frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $writer.Write([byte] $dim); $writer.Write([byte] $dim)
    $writer.Write([byte] 0); $writer.Write([byte] 0)
    $writer.Write([UInt16] 1); $writer.Write([UInt16] 32)
    $writer.Write([UInt32] $f[1].Length); $writer.Write([UInt32] $offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $writer.Write([byte[]] $f[1]) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'almatter-logo.ico'), $ico.ToArray())
$writer.Dispose()

Write-Host "Wrote almatter-logo.png and almatter-logo.ico ($($frames.Count) sizes)"
