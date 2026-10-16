# Renders tcapture.ico (Tnote series style: pale yellow tile + blue glyph)
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

    # tile
    $tile = Rounded (1*$u) (1*$u) (62*$u) (62*$u) (12*$u)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(247, 235, 188))), $tile)
    $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(212, 189, 63)), ([Math]::Max(1, 2*$u))), $tile)

    # captured picture (white card, blue outline, mountain + sun)
    $card = Rounded (19*$u) (21*$u) (26*$u) (22*$u) (3*$u)
    $g.FillPath([Drawing.Brushes]::White, $card)
    $g.DrawPath((New-Object Drawing.Pen $blue, ([Math]::Max(1, 2.5*$u))), $card)
    if ($s -ge 24) {
        $lb = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(140, 185, 225))
        $g.FillPolygon($lb, [Drawing.PointF[]]@(
            (New-Object Drawing.PointF (21.5*$u), (40.5*$u)), (New-Object Drawing.PointF (29*$u), (31*$u)),
            (New-Object Drawing.PointF (34*$u), (36*$u)), (New-Object Drawing.PointF (37*$u), (33*$u)),
            (New-Object Drawing.PointF (42.5*$u), (40.5*$u))))
        $g.FillEllipse((New-Object Drawing.SolidBrush $blue), (36*$u), (25*$u), (5*$u), (5*$u))
    }

    # selection corner brackets
    $pen = New-Object Drawing.Pen $blue, ([Math]::Max(1.5, 4*$u))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $a = 11*$u; $b = 53*$u; $l = 9*$u
    foreach ($c in @(@($a,$a,1,1), @($b,$a,-1,1), @($a,$b,1,-1), @($b,$b,-1,-1))) {
        $x = $c[0]; $y = $c[1]
        $g.DrawLines($pen, [Drawing.PointF[]]@(
            (New-Object Drawing.PointF $x, ($y + $c[3]*$l)), (New-Object Drawing.PointF $x, $y),
            (New-Object Drawing.PointF ($x + $c[2]*$l), $y)))
    }
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
