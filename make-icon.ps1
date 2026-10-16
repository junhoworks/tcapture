# Renders tcapture.ico (Tnote series style: blue glyph on transparent background)
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'tcapture.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Rounded($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Render([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $u = $s / 64.0
    $blue = [Drawing.Color]::FromArgb(58, 120, 180)

    # transparent background; enlarge glyph to fill the canvas
    $g.TranslateTransform(32*$u, 32*$u); $g.ScaleTransform(1.26, 1.26); $g.TranslateTransform(-32.5*$u, -31.5*$u)

    # selection box (outline only)
    $box = Rounded (10*$u) (10*$u) (34*$u) (34*$u) (5*$u)
    $g.DrawPath((New-Object Drawing.Pen $blue, ([Math]::Max(1.5, 4*$u))), $box)

    # camera overlapping bottom-right corner, with a transparent gap ring
    $bg = [Drawing.Color]::Transparent
    $body = Rounded (27*$u) (33*$u) (30*$u) (21*$u) (5*$u)
    $hump = Rounded (33*$u) (28*$u) (11*$u) (8*$u) (2.5*$u)
    $gap = New-Object Drawing.Pen $bg, ([Math]::Max(1.5, 6*$u)); $gap.LineJoin = 'Round'
    $g.CompositingMode = 'SourceCopy'; $g.DrawPath($gap, $hump); $g.DrawPath($gap, $body); $g.CompositingMode = 'SourceOver'
    $bb = New-Object Drawing.SolidBrush $blue
    $g.FillPath($bb, $hump); $g.FillPath($bb, $body)
    $g.FillEllipse([Drawing.Brushes]::White, (35.5*$u), (36.5*$u), (13*$u), (13*$u))
    $g.FillEllipse($bb, (38.5*$u), (39.5*$u), (7*$u), (7*$u))
    if ($s -ge 32) { $g.FillEllipse([Drawing.Brushes]::White, (50*$u), (36.5*$u), (3*$u), (3*$u)) }
    $g.Dispose(); $bmp
}

# write ICO with PNG frames
$frames = foreach ($s in $sizes) {
    $ms = New-Object IO.MemoryStream; $bm = Render $s
    $bm.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bm.Save((Join-Path $PSScriptRoot 'icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $bm.Dispose(); ,@($s, $ms.ToArray())
}
$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $d = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
"wrote $out"
