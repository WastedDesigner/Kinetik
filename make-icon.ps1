# Generates icon.ico (16–256 px) for PC Stats Bar: a gradient tile with rising stat bars on a taskbar line.
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

    # Tile: violet → cyan diagonal gradient with a soft top highlight
    $m = [Math]::Max(0.5, $s * 0.03)
    $tile = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.23)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 108, 76, 255)), ([System.Drawing.Color]::FromArgb(255, 0, 198, 255)), 45
    $g.FillPath($grad, $tile)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.FillPath($hl, $tile)

    # Rising bars
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(185, 255, 255, 255))
    $base = $s * 0.70; $bw = $s * 0.14; $gap = $s * 0.075; $x0 = ($s - (3 * $bw + 2 * $gap)) / 2
    $heights = 0.20, 0.33, 0.46
    for ($i = 0; $i -lt 3; $i++) {
        $h = $s * $heights[$i]; $x = $x0 + $i * ($bw + $gap)
        $bar = RoundRect $x ($base - $h) $bw $h ($bw * 0.35)
        if ($i -eq 2) { $g.FillPath($white, $bar) } else { $g.FillPath($soft, $bar) }
    }

    # Taskbar line
    $line = RoundRect ($s * 0.2) ($s * 0.76) ($s * 0.6) ([Math]::Max(1, $s * 0.07)) ($s * 0.035)
    $g.FillPath($white, $line)

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
