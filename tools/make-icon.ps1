# Draws the BlackNote icon (dark rounded tile, text lines, blue caret)
# and writes a multi-size .ico (PNG-compressed entries).
param([string]$Out = (Join-Path $PSScriptRoot '..\src\BlackNote.ico'))

Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Get-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 256.0

    $tile = New-RoundRect (8*$k) (8*$k) (240*$k) (240*$k) (46*$k)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 28, 32))), $tile)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 70, 70, 82)), ([Math]::Max(1, 6*$k))
    $g.DrawPath($pen, $tile)

    $line = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 214, 222))
    $h = [Math]::Max(2, 22*$k)
    $rows = @(@(52, 152), @(98, 124), @(144, 140))
    foreach ($r in $rows) {
        $p = New-RoundRect (50*$k) ($r[0]*$k) ($r[1]*$k) $h ($h/2)
        $g.FillPath($line, $p)
    }
    # last short line + blue caret
    $p = New-RoundRect (50*$k) (190*$k) (70*$k) $h ($h/2)
    $g.FillPath($line, $p)
    $caret = New-RoundRect (136*$k) (178*$k) ([Math]::Max(2, 14*$k)) (46*$k) ([Math]::Max(1, 7*$k))
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 64, 156, 255))), $caret)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # classic DIB entry: BITMAPINFOHEADER + BGRA bottom-up + empty AND mask
        $w = New-Object System.IO.BinaryWriter $ms
        $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2))
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
        $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $w.Write((New-Object byte[] ($maskRow * $s)))
        $w.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { ,(Get-IconPng $s) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out) | Join-Path -ChildPath (Split-Path $Out -Leaf)))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Output "Icon written: $Out"
