param([string]$OutIco, [string]$OutPng, [string]$OutSmall)
Add-Type -AssemblyName System.Drawing

function Pt([double]$x, [double]$y) { return New-Object System.Drawing.PointF([float]$x, [float]$y) }

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square, deep blue gradient (sky)
    $r = [float]($S * 0.20); $m = [float]($S * 0.02); $w = [float]($S - 2 * $m); $d = [float](2 * $r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90); $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF(0, 0, $S, $S)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 30, 92, 190), [System.Drawing.Color]::FromArgb(255, 8, 30, 76), 90)
    $g.FillPath($bg, $path)
    $g.SetClip($path)

    $cx = [double]($S * 0.5); $cy = [double]($S * 0.5)
    $amber = [System.Drawing.Color]::FromArgb(255, 255, 176, 0)

    # --- pseudo-fuselage: a double arrow <-> (nose and tail heads, shaft swelling at the centre)
    $L = $S * 0.07; $R = $S * 0.93
    $hl = $S * 0.20            # head length
    $hh = $S * 0.17            # head half height
    $sh = $S * 0.052           # shaft half height at the ends
    $sc = $S * 0.082           # shaft half height at the centre
    $pts = @(
        (Pt $L $cy),
        (Pt ($L + $hl) ($cy - $hh)),
        (Pt ($L + $hl * 0.78) ($cy - $sh)),
        (Pt ($cx - $S * 0.14) ($cy - $sh * 1.05)),
        (Pt $cx ($cy - $sc)),
        (Pt ($cx + $S * 0.14) ($cy - $sh * 1.05)),
        (Pt ($R - $hl * 0.78) ($cy - $sh)),
        (Pt ($R - $hl) ($cy - $hh)),
        (Pt $R $cy),
        (Pt ($R - $hl) ($cy + $hh)),
        (Pt ($R - $hl * 0.78) ($cy + $sh)),
        (Pt ($cx + $S * 0.14) ($cy + $sh * 1.05)),
        (Pt $cx ($cy + $sc)),
        (Pt ($cx - $S * 0.14) ($cy + $sh * 1.05)),
        (Pt ($L + $hl * 0.78) ($cy + $sh)),
        (Pt ($L + $hl) ($cy + $hh))
    )
    $arrow = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.RectangleF(0, [float]($cy - $hh), $S, [float](2 * $hh))), [System.Drawing.Color]::FromArgb(255, 255, 205, 70), [System.Drawing.Color]::FromArgb(255, 238, 140, 0), 90)
    $g.FillPolygon($arrow, [System.Drawing.PointF[]]$pts)
    if ($S -ge 48) {
        $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 120, 60, 0), [float][Math]::Max(1.0, $S * 0.008))
        $g.DrawPolygon($edge, [System.Drawing.PointF[]]$pts)
    }

    # --- main rotor: four blades with motion blur (a fading sweep behind each blade)
    $rad = $S * 0.43; $root = $S * 0.05; $bw = $S * 0.014
    $sweeps = if ($S -le 24) { 6 } else { 26 }
    $step = if ($S -le 24) { 6.0 } else { 1.45 }
    $base = 68
    for ($b = 0; $b -lt 4; $b++) {
        $a0 = $base + 90 * $b
        for ($j = $sweeps - 1; $j -ge 0; $j--) {
            $ang = $a0 - $j * $step
            $f = 1.0 - $j / [double]$sweeps
            $alpha = [int](150 * [Math]::Pow($f, 2.4))
            if ($j -eq 0) { $alpha = 205 }
            $col = [System.Drawing.Color]::FromArgb($alpha, 236, 244, 255)
            $br = New-Object System.Drawing.SolidBrush($col)
            $state = $g.Save()
            $g.TranslateTransform([float]$cx, [float]$cy)
            $g.RotateTransform([float]$ang)
            $bp = @((Pt $root (-$bw * 0.8)), (Pt ($rad - $bw * 1.6) (-$bw)), (Pt $rad (-$bw * 0.35)), (Pt $rad ($bw * 0.35)), (Pt ($rad - $bw * 1.6) $bw), (Pt $root ($bw * 0.8)))
            $g.FillPolygon($br, [System.Drawing.PointF[]]$bp)
            $g.Restore($state)
            $br.Dispose()
        }
    }
    # faint rotor disc and rim
    $disc = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(16, 255, 255, 255))
    $g.FillEllipse($disc, [float]($cx - $rad), [float]($cy - $rad), [float](2 * $rad), [float](2 * $rad))

    # hub
    $hubR = [float]($S * 0.055)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 14, 40, 90))), [float]($cx - $hubR), [float]($cy - $hubR), [float](2 * $hubR), [float](2 * $hubR))
    $hubR2 = [float]($S * 0.028)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 240, 250))), [float]($cx - $hubR2), [float]($cy - $hubR2), [float](2 * $hubR2), [float](2 * $hubR2))

    $g.ResetClip()
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256 -and $OutPng) { $b.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($s -eq 32 -and $OutSmall) {
        # 32 px shown at 8x for a legibility check
        $big = New-Object System.Drawing.Bitmap(256, 256)
        $gg = [System.Drawing.Graphics]::FromImage($big); $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $gg.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240)); $gg.DrawImage($b, 0, 0, 256, 256); $gg.Dispose()
        $big.Save($OutSmall, [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
    }
    $b.Dispose()
}
$fs = [System.IO.File]::Create($OutIco)
$bw2 = New-Object System.IO.BinaryWriter($fs)
$bw2.Write([uint16]0); $bw2.Write([uint16]1); $bw2.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $wb = if ($s -ge 256) { 0 } else { $s }
    $bw2.Write([byte]$wb); $bw2.Write([byte]$wb); $bw2.Write([byte]0); $bw2.Write([byte]0)
    $bw2.Write([uint16]1); $bw2.Write([uint16]32)
    $bw2.Write([uint32]$pngs[$i].Length); $bw2.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw2.Write($p) }
$bw2.Close(); $fs.Close()
"icon written: $OutIco"
