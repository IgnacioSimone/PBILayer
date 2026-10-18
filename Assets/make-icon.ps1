<#
.SYNOPSIS
    Genera PBIContext.ico e icon-256.png a partir de la geometría de icon.svg.

.DESCRIPTION
    WPF y Windows no leen SVG, así que las formas del icono (cuadrado redondeado oscuro, tres capas amarillas
    y cuatro barras blancas) están repetidas acá como polígonos. Si cambiás icon.svg, actualizá las listas
    de puntos de abajo y volvé a correr este script.

    Uso:  powershell -File Assets\make-icon.ps1
#>
Add-Type -AssemblyName System.Drawing
$outDir = $PSScriptRoot

function New-Poly([double[]]$coords, [double]$s) {
    $pts = for ($i = 0; $i -lt $coords.Count; $i += 2) { New-Object Drawing.PointF ([single]($coords[$i] * $s)), ([single]($coords[$i + 1] * $s)) }
    return , $pts
}

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Capas (de arriba hacia abajo) y barras, en las coordenadas de 128x128 del SVG.
$yellow = [Drawing.Color]::FromArgb(255, 0xF6, 0xC9, 0x44)
$amber = [Drawing.Color]::FromArgb(255, 0xD9, 0xA7, 0x1E)
$layers = @(
    @{ Color = $yellow; Points = @(24, 41, 64, 20, 104, 41, 64, 62) },
    @{ Color = $amber;  Points = @(24, 51, 64, 72, 104, 51, 104, 63, 64, 84, 24, 63) },
    @{ Color = $yellow; Points = @(24, 70, 64, 91, 104, 70, 104, 82, 64, 103, 24, 82) }
)
$bars = @(
    @(34, 72, 34, 58, 44, 63.25, 44, 77.25),
    @(49, 80, 49, 56, 59, 61.25, 59, 85.25),
    @(64, 88, 64, 50, 74, 44.75, 74, 82.75),
    @(79, 80, 79, 56, 89, 50.75, 89, 74.75)
)

function Render([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $s = $size / 128.0

    $bg = New-RoundedPath (3 * $s) (3 * $s) (122 * $s) (122 * $s) (29 * $s)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0x12, 0x13, 0x13))), $bg)

    # Borde interior casi invisible (blanco al 12 %), solo se nota en tamaños grandes.
    if ($size -ge 48) {
        $edge = New-RoundedPath (3.75 * $s) (3.75 * $s) (120.5 * $s) (120.5 * $s) (28.25 * $s)
        $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(31, 255, 255, 255)), ([single](1.5 * $s))), $edge)
    }

    foreach ($layer in $layers) {
        $g.FillPolygon((New-Object Drawing.SolidBrush $layer.Color), (New-Poly $layer.Points $s))
    }
    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    foreach ($bar in $bars) { $g.FillPolygon($white, (New-Poly $bar $s)) }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$entries = @()
foreach ($sz in $sizes) {
    $bmp = Render $sz
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $entries += , @($sz, $ms.ToArray())
    if ($sz -eq 256) { $bmp.Save((Join-Path $outDir 'icon-256.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# .ico con imágenes PNG adentro (válido desde Windows Vista).
$fs = [IO.File]::Create((Join-Path $outDir 'PBIContext.ico'))
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e[0] -ge 256) { 0 } else { $e[0] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e[1].Length); $bw.Write([uint32]$offset)
    $offset += $e[1].Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Close(); $fs.Close()
"Iconos generados en $outDir ($($entries.Count) tamaños)"
