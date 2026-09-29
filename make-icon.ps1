# Generates icon.ico (16–256 px) for Kinetik: a three-bladed fan on an orange gradient tile, matching the default tray icon.
# Run: powershell -ExecutionPolicy Bypass -File make-icon.ps1
Add-Type -AssemblyName System.Drawing

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    # Tile: Kinetik orange diagonal gradient (matches the default tray icon)
    $m = [Math]::Max(0.5, $s * 0.03)
    $tile = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.23)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $c1 = [System.Drawing.Color]::FromArgb(255, 255, 106, 43); $c2 = [System.Drawing.Color]::FromArgb(255, 255, 140, 60)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 45
    $g.FillPath($grad, $tile)

    # Three-bladed fan
    $cx = $s / 2
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    for ($q = 0; $q -lt 3; $q++) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddEllipse($cx - $s * 0.106, $cx - $s * 0.3625, $s * 0.212, $s * 0.35)
        $mx = New-Object System.Drawing.Drawing2D.Matrix
        $mx.RotateAt($q * 120, (New-Object System.Drawing.PointF $cx, $cx))
        $p.Transform($mx); $g.FillPath($white, $p)
    }
    # Hub: a ring in the tile colour around a small dot
    $hub = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 123, 51))
    $hr = $s * 0.075; $dr = $s * 0.037
    $g.FillEllipse($hub, $cx - $hr, $cx - $hr, $hr * 2, $hr * 2)
    $g.FillEllipse($white, $cx - $dr, $cx - $dr, $dr * 2, $dr * 2)

    $g.Dispose(); return $bmp
}

# Write a multi-size .ico with PNG-compressed entries
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw $s; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())

# Preview for README / checking
$prev = Draw 256; $prev.Save((Join-Path $PSScriptRoot 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png); $prev.Dispose()
