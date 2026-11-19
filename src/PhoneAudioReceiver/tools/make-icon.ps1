# Phone Audio Receiver - Copyright (c) 2026 Mayyas Banat - MIT License
# Generates Assets\AppIcon.ico (16-256 px), plus an optional 256 px PNG and a preview sheet.
# Design: violet-to-cyan squircle with a white audio waveform.
param(
    [string]$OutIco = (Join-Path $PSScriptRoot '..\Assets\AppIcon.ico'),
    [string]$Png,
    [string]$Preview
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(0.5, $s * 0.03)
    $size = $s - 2 * $inset
    $tile = New-RoundedPath $inset $inset $size $size ($size * 0.26)

    $rect = New-Object System.Drawing.RectangleF $inset, $inset, $size, $size
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 0x7A, 0x5C, 0xFF)), `
        ([System.Drawing.Color]::FromArgb(255, 0x12, 0xC2, 0xF5)), 45.0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(255, 0x8B, 0x5C, 0xF6),
        [System.Drawing.Color]::FromArgb(255, 0x5B, 0x7C, 0xFA),
        [System.Drawing.Color]::FromArgb(255, 0x10, 0xC8, 0xF0))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $grad.InterpolationColors = $blend
    $g.FillPath($grad, $tile)

    if ($s -ge 32) {
        $g.SetClip($tile)
        $glossArea = New-Object System.Drawing.RectangleF $inset, $inset, $size, ($size * 0.55)
        $glossRamp = New-Object System.Drawing.RectangleF $inset, ($inset - 1), $size, ($size * 0.55 + 2)
        $glossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $glossRamp, `
            ([System.Drawing.Color]::FromArgb(48, 255, 255, 255)), `
            ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
        $g.FillRectangle($glossBrush, $glossArea)
        $g.ResetClip()
    }

    if ($s -le 24) {
        $heights = @(0.34, 0.62, 0.34); $barW = 0.15; $gap = 0.085
    } else {
        $heights = @(0.24, 0.46, 0.64, 0.46, 0.24); $barW = 0.085; $gap = 0.055
    }
    $n = $heights.Count
    $total = ($n * $barW + ($n - 1) * $gap) * $size
    $x = $inset + ($size - $total) / 2
    $cy = $inset + $size / 2
    $white = [System.Drawing.Brushes]::White
    foreach ($h in $heights) {
        $bw = $barW * $size; $bh = $h * $size
        $bar = New-RoundedPath $x ($cy - $bh / 2) $bw $bh ($bw / 2)
        $g.FillPath($white, $bar)
        $x += $bw + $gap * $size
    }
    $g.Dispose()
    $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0); $w.Write([int]0)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [byte[]]$bytes = $ms.ToArray()
    } else {
        [byte[]]$bytes = Get-DibBytes $bmp
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $bytes }
}

New-Item -ItemType Directory -Force (Split-Path $OutIco) | Out-Null
$fs = [System.IO.File]::Create($OutIco)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Bytes.Length); $w.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Bytes, 0, $img.Bytes.Length) }
$w.Dispose()
"Wrote $OutIco"

if ($Png) {
    $big = Draw-Icon 256
    New-Item -ItemType Directory -Force (Split-Path $Png) | Out-Null
    $big.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png)
    $big.Dispose()
    "Wrote $Png"
}

if ($Preview) {
    $show = 16, 24, 32, 48, 64, 128, 256
    $pad = 24
    $width = ($show | Measure-Object -Sum).Sum + $pad * ($show.Count + 1)
    $canvas = New-Object System.Drawing.Bitmap ([int]$width), ([int]((256 + 2 * $pad) * 2))
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $bands = @([System.Drawing.Color]::FromArgb(255, 32, 32, 36), [System.Drawing.Color]::FromArgb(255, 243, 243, 243))
    for ($band = 0; $band -lt 2; $band++) {
        $top = $band * (256 + 2 * $pad)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush $bands[$band]), 0, $top, $width, 256 + 2 * $pad)
        $x = $pad
        foreach ($s in $show) {
            $img = Draw-Icon $s
            $g.DrawImageUnscaled($img, $x, $top + $pad + (256 - $s))
            $img.Dispose()
            $x += $s + $pad
        }
    }
    $canvas.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    "Preview $Preview"
}
