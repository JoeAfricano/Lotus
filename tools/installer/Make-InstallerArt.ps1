param([string]$Root = "C:\Projetos\Lotus")
# Builds the installer artwork from the app's own concept art (PNG, several DPI sizes).
Add-Type -AssemblyName System.Drawing
$out = Join-Path $Root "tools\installer\art"; New-Item -ItemType Directory -Force $out | Out-Null
$sculpture = [System.Drawing.Image]::FromFile((Join-Path $Root "src\Lotus.App\Assets\Concept\lotus-sculpture.png"))
$ambient = [System.Drawing.Image]::FromFile((Join-Path $Root "src\Lotus.App\Assets\Concept\lotus-ambient.png"))
$plum = [System.Drawing.Color]::FromArgb(255, 0x1A, 0x0F, 0x18)

function New-Canvas($w, $h) { $b = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'; return $b, $g }
# Draws $img to fill w x h (cover), centred on the horizontal focus fx (0..1).
function Draw-Cover($g, $img, $w, $h, $fx = 0.5, $fy = 0.5) {
    $scale = [Math]::Max($w / $img.Width, $h / $img.Height); $sw = $w / $scale; $sh = $h / $scale
    $sx = [Math]::Min([Math]::Max($img.Width * $fx - $sw / 2, 0), $img.Width - $sw); $sy = [Math]::Min([Math]::Max($img.Height * $fy - $sh / 2, 0), $img.Height - $sh)
    $g.DrawImage($img, (New-Object System.Drawing.RectangleF 0, 0, $w, $h), (New-Object System.Drawing.RectangleF $sx, $sy, $sw, $sh), 'Pixel')
}
function Fade($g, $w, $h, $fromY, $alphaTop, $alphaBottom) {
    $r = New-Object System.Drawing.Rectangle 0, $fromY, $w, ($h - $fromY)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb($alphaTop, $plum)), ([System.Drawing.Color]::FromArgb($alphaBottom, $plum)), 90
    $g.FillRectangle($br, $r); $br.Dispose()
}

# Side image (welcome / finish), 164x314 at 100% and larger sizes for high DPI.
foreach ($s in 100, 150, 200, 250) {
    $w = [int](164 * $s / 100); $h = [int](314 * $s / 100)
    $b, $g = New-Canvas $w $h
    $g.Clear($plum); Draw-Cover $g $sculpture $w $h 0.5 0.55
    Fade $g $w $h ([int]($h * 0.55)) 0 235
    # Gold "Lotus" word and the rose tagline at the bottom.
    $gold = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, ([int]($h * 0.78)), $w, ([int]($h * 0.12))), ([System.Drawing.Color]::FromArgb(255, 0xF3, 0xD2, 0xB0)), ([System.Drawing.Color]::FromArgb(255, 0xC9, 0x8A, 0x5C)), 90
    $f = New-Object System.Drawing.Font "Georgia", ([float](30 * $s / 100)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $t = New-Object System.Drawing.Font "Segoe UI Semibold", ([float](9.5 * $s / 100)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'
    $g.DrawString("Lotus", $f, $gold, (New-Object System.Drawing.RectangleF 0, ($h * 0.79), $w, ($h * 0.12)), $fmt)
    $g.DrawString("PLAY BETTER.", $t, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF0, 0x7A, 0x9C))), (New-Object System.Drawing.RectangleF 0, ($h * 0.905), $w, ($h * 0.06)), $fmt)
    $b.Save((Join-Path $out "wizard-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
}

# Small image (top-right of inner pages): the Lotus icon, 55x55 at 100%.
# The 256 px frame of the .ico is stored as PNG: read the icon directory and load the largest frame directly.
$ico = [IO.File]::ReadAllBytes((Join-Path $Root "src\Lotus.App\Assets\Lotus.ico")); $count = [BitConverter]::ToUInt16($ico, 4); $best = $null; $bestSize = 0
for ($i = 0; $i -lt $count; $i++) { $e = 6 + 16 * $i; $sz = if ($ico[$e] -eq 0) { 256 } else { $ico[$e] }; if ($sz -gt $bestSize) { $bestSize = $sz; $best = $e } }
$len = [BitConverter]::ToInt32($ico, $best + 8); $off = [BitConverter]::ToInt32($ico, $best + 12)
$logo = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (,[byte[]]$ico[$off..($off + $len - 1)])))
$icon = $logo
foreach ($s in 100, 150, 200, 250) {
    $n = [int](55 * $s / 100); $b, $g = New-Canvas $n $n
    $g.Clear([System.Drawing.Color]::Transparent); $g.DrawImage($logo, 0, 0, $n, $n)
    $b.Save((Join-Path $out "small-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
}

# Page background: the ambient art, darkened towards the left where the text sits.
$w = 1200; $h = 900; $b, $g = New-Canvas $w $h
$g.Clear($plum); Draw-Cover $g $ambient $w $h 0.62 0.5
$r = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(245, $plum)), ([System.Drawing.Color]::FromArgb(150, $plum)), 0
$g.FillRectangle($br, $r)
$b.Save((Join-Path $out "background.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
$sculpture.Dispose(); $ambient.Dispose(); $logo.Dispose(); $icon.Dispose()
Get-ChildItem $out | ForEach-Object { "{0} {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
