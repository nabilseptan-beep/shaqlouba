# Draws assets\icon.ico and writes a preview PNG next to it. Run once; the .ico is committed.
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\assets\icon.ico'),
    [string]$PreviewFile = ''
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$size, [single]$radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-Glyph($g, [string]$text, [string]$fontName, [single]$emSize, [single]$cx, [single]$cy, $brush) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $family = New-Object Drawing.FontFamily $fontName
    $format = New-Object Drawing.StringFormat
    $path.AddString($text, $family, [int][Drawing.FontStyle]::Bold, $emSize, (New-Object Drawing.PointF 0, 0), $format)
    $bounds = $path.GetBounds()
    $move = New-Object Drawing.Drawing2D.Matrix
    $move.Translate($cx - $bounds.X - $bounds.Width / 2, $cy - $bounds.Y - $bounds.Height / 2)
    $path.Transform($move)
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    [single]$s = $size
    $tile = New-RoundedPath $s ([Math]::Max(2.0, $s * 0.2))

    # Two halves split on the diagonal: one language each.
    $g.SetClip($tile)
    $emerald = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 5, 150, 105))
    $blue = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 37, 78, 190))
    $g.FillRectangle($emerald, 0, 0, $s, $s)
    $g.FillPolygon($blue, [Drawing.PointF[]]@(
        (New-Object Drawing.PointF $s, 0),
        (New-Object Drawing.PointF $s, $s),
        (New-Object Drawing.PointF 0, $s)))
    $g.ResetClip()

    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $ain = [string][char]0x0639
    Add-Glyph $g $ain 'Segoe UI' ($s * 0.60) ($s * 0.31) ($s * 0.34) $white
    Add-Glyph $g 'A' 'Segoe UI' ($s * 0.56) ($s * 0.70) ($s * 0.69) $white

    $g.Dispose()
    return $bitmap
}

# 32-bit DIB frame: bottom-up BGRA pixels followed by a 1-bit AND mask.
function Get-DibFrame($bitmap) {
    $size = $bitmap.Width
    $stream = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $stream
    $maskStride = [int]([Math]::Ceiling($size / 32.0)) * 4

    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($size * $size * 4 + $maskStride * $size)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)

    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bitmap.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($bitmap.GetPixel($x, $y).A -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x % 8)) }
        }
        $w.Write($row)
    }
    $w.Flush()
    return ,$stream.ToArray()
}

function Get-PngFrame($bitmap) {
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $bytes = if ($size -ge 256) { Get-PngFrame $bitmap } else { Get-DibFrame $bitmap }
    $bitmap.Dispose()
    , @($size, $bytes)
}

$OutFile = [IO.Path]::GetFullPath($OutFile)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutFile))
$file = New-Object IO.BinaryWriter ([IO.File]::Create($OutFile))
$file.Write([int16]0); $file.Write([int16]1); $file.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $size = $frame[0]; $bytes = $frame[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $file.Write([byte]$dim); $file.Write([byte]$dim); $file.Write([byte]0); $file.Write([byte]0)
    $file.Write([int16]1); $file.Write([int16]32); $file.Write([int]$bytes.Length); $file.Write([int]$offset)
    $offset += $bytes.Length
}
foreach ($frame in $frames) { $file.Write($frame[1]) }
$file.Close()
"Wrote $OutFile"

if ($PreviewFile) {
    # Each size shown at 4x (nearest neighbour) plus the large one, to judge it the way the tray shows it.
    $sheet = New-Object Drawing.Bitmap 880, 300
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::FromArgb(255, 40, 40, 40))
    $g.InterpolationMode = 'NearestNeighbor'
    $g.PixelOffsetMode = 'Half'
    $x = 10
    foreach ($size in 16, 20, 24, 32) {
        $b = New-IconBitmap $size
        $g.DrawImage($b, $x, 10, $size * 5, $size * 5)
        $g.DrawImage($b, $x, 190, $size, $size)
        $b.Dispose()
        $x += $size * 5 + 12
    }
    $g.InterpolationMode = 'HighQualityBicubic'
    $b = New-IconBitmap 256
    $g.DrawImage($b, $x, 10, 256, 256)
    $b.Dispose()
    $g.Dispose()
    $sheet.Save($PreviewFile, [Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    "Preview $PreviewFile"
}
