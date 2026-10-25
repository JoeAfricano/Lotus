param([string]$Out = (Join-Path $PSScriptRoot "..\src\Lotus.App\Assets\Lotus.ico"))
# Renders the Lotus vector mark (same geometry as MainWindow.xaml) into a multi-size .ico (PNG entries).
Add-Type -AssemblyName System.Drawing
$petalsOuter = "M31,50 C18,50 7,43 2,31 C14,30 25,37 31,50 Z M33,50 C46,50 57,43 62,31 C50,30 39,37 33,50 Z M30,48 C19,42 13,30 16,17 C25,23 30,34 30,48 Z M34,48 C45,42 51,30 48,17 C39,23 34,34 34,48 Z"
$center = "M32,7 C41,17 44,32 32,48 C20,32 23,17 32,7 Z"
$arch = "M32,17 C36.5,23.5 37.5,31.5 32,39 C26.5,31.5 27.5,23.5 32,17 Z"
$sparkles = "M32,0.5 L33.5,3.5 L32,6 L30.5,3.5 Z M32,51 L34,55 L32,62.5 L30,55 Z M9,14 L10.4,16.5 L9,19 L7.6,16.5 Z M55,14 L56.4,16.5 L55,19 L53.6,16.5 Z"

function PathOf([string]$data, [float]$s, [float]$ox, [float]$oy) {
    $g = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tokens = [regex]::Matches($data, '[MCLZ]|-?\d+(\.\d+)?') | ForEach-Object { $_.Value }
    $i = 0; $cur = $null; $start = $null
    function P($x, $y) { New-Object System.Drawing.PointF(($ox + [float]$x * $s), ($oy + [float]$y * $s)) }
    while ($i -lt $tokens.Count) {
        switch ($tokens[$i]) {
            'M' { $g.StartFigure(); $cur = P $tokens[$i+1] $tokens[$i+2]; $start = $cur; $i += 3 }
            'L' { $n = P $tokens[$i+1] $tokens[$i+2]; $g.AddLine($cur, $n); $cur = $n; $i += 3 }
            'C' { $a = P $tokens[$i+1] $tokens[$i+2]; $b = P $tokens[$i+3] $tokens[$i+4]; $n = P $tokens[$i+5] $tokens[$i+6]; $g.AddBezier($cur, $a, $b, $n); $cur = $n; $i += 7 }
            'Z' { $g.CloseFigure(); $cur = $start; $i += 1 }
            default { $i += 1 }
        }
    }
    $g
}
function Rounded([float]$x, [float]$y, [float]$w, [float]$r) {
    $g = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = 2 * $r
    $g.AddArc($x, $y, $d, $d, 180, 90); $g.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $g.AddArc($x + $w - $d, $y + $w - $d, $d, $d, 0, 90); $g.AddArc($x, $y + $w - $d, $d, $d, 90, 90); $g.CloseFigure(); $g
}
function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gr = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.SmoothingMode = 'AntiAlias'; $gr.PixelOffsetMode = 'HighQuality'; $gr.Clear([System.Drawing.Color]::Transparent)
    $tile = Rounded 0 0 ($size - 1) ($size * 0.22)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), [System.Drawing.Color]::FromArgb(255, 46, 16, 34), [System.Drawing.Color]::FromArgb(255, 16, 7, 13))
    $gr.FillPath($bg, $tile)
    $gr.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 238, 193, 168)), ([Math]::Max(1, $size / 96))), $tile)
    $s = $size * 0.78 / 64; $o = $size * 0.11
    $petal = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, $o), (New-Object System.Drawing.PointF 0, ($o + 50 * $s)), [System.Drawing.Color]::FromArgb(255, 240, 71, 154), [System.Drawing.Color]::FromArgb(255, 94, 10, 46))
    $gold = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, $o), (New-Object System.Drawing.PointF 0, ($o + 64 * $s)), [System.Drawing.Color]::FromArgb(255, 248, 221, 203), [System.Drawing.Color]::FromArgb(255, 201, 132, 104))
    $outer = PathOf $petalsOuter $s $o $o; $c = PathOf $center $s $o $o
    $gr.FillPath($petal, $outer); $gr.FillPath($petal, $c)
    if ($size -ge 32) { $gr.FillPath($gold, (PathOf $sparkles $s $o $o)) }
    $pen = New-Object System.Drawing.Pen($gold, [float]([Math]::Max(1.1, 2.4 * $s))); $pen.LineJoin = 'Round'
    $gr.DrawPath($pen, $outer); $gr.DrawPath($pen, $c)
    if ($size -ge 48) { $gr.DrawPath($pen, (PathOf $arch $s $o $o)) }
    $gr.Dispose(); $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); , $ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($z in $sizes) { , (Render $z) }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $z = $sizes[$k]; $len = $images[$k].Length
    $w.Write([byte]($(if ($z -ge 256) { 0 } else { $z }))); $w.Write([byte]($(if ($z -ge 256) { 0 } else { $z })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset); $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
[System.IO.File]::WriteAllBytes(([System.IO.Path]::ChangeExtension($Out, ".png")), $images[-1])
Write-Output "Icon written: $Out"
