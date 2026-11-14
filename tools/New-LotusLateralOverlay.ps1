# Builds src\Lotus.Infrastructure\Integrations\Lotus-Lateral.ovl: a compact vertical column for the top-left corner.
# Reuses the data sources of the player's "Lotus Neon" layout (same readings, fonts and refresh); only the layers are new.
# The two existing layouts (Lotus.ovl, Lotus-Neon.ovl) are read, never written.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'src\Lotus.Infrastructure\Integrations'
$latin = [Text.Encoding]::GetEncoding(28591)
$neon = $latin.GetString([IO.File]::ReadAllBytes((Join-Path $dir 'Lotus-Neon.ovl')))
$sources = $neon.Substring($neon.IndexOf('[Source0]'), $neon.IndexOf('[Layer0]') - $neon.IndexOf('[Source0]')).TrimEnd() + "`r`n"
$sourceCount = ([regex]::Matches($sources, '\[Source\d+\]')).Count

$W = 212; $H = 352
$rose = 'FF3480'; $text = 'FFF5FA'; $muted = 'BEB8C1'; $gold = 'EEC1A8'
# RivaTuner's overlay font drops accented letters such as "é" (seen in game, 14/11/2026): labels stay ASCII.
# Name, Text, X, Y, ExtentX, ExtentY, Size (% of the font), Color
$layers = @(
    @('Panel', "<B=$W,$H>", 10, 10, $W, $H, 100, '99120A12'),
    @('Accent', "<B=3,$H>", 10, 10, 3, $H, 100, "FF$rose"),
    @('Brand', 'LOTUS', 24, 18, 80, 16, 80, $gold),
    @('FPS', '<FR>', 22, 34, 120, 58, 330, $text),
    @('FPS label', 'FPS', 150, 70, 40, 18, 90, $rose),
    @('Frametime', '<FT> ms', 24, 98, 90, 20, 100, $text),
    @('1 percent low', '1% %FPS Low1 Live%', 116, 98, 92, 20, 100, $text),
    @('Frametime graph', '<G=Frametime,0,0,1,0,33.3,0>', 24, 124, 184, 44, 73, $rose),
    @('GPU label', 'GPU', 24, 182, 44, 20, 95, $gold),
    @('GPU', '%GPU Load%%  %GPU Temp% °C', 76, 182, 132, 20, 100, $text),
    @('GPU bar', '<G=GPU Load,0,0,0,0,100,9>', 24, 206, 184, 6, 73, $rose),
    @('CPU label', 'CPU', 24, 222, 44, 20, 95, $gold),
    @('CPU', '%CPU Load%%  %CPU Temp% °C', 76, 222, 132, 20, 100, $text),
    @('CPU bar', '<G=CPU Load,0,0,0,0,100,9>', 24, 246, 184, 6, 73, $rose),
    @('VRAM label', 'VRAM', 24, 262, 50, 20, 95, $gold),
    @('VRAM', '%VRAM Load%%', 76, 262, 132, 20, 100, $text),
    @('RAM label', 'RAM', 24, 288, 50, 20, 95, $gold),
    @('RAM', '%RAM Load%%', 76, 288, 132, 20, 100, $text),
    @('Average label', 'AVG', 24, 318, 44, 18, 85, $gold),
    @('Average', '%FPS Avg Live%', 76, 318, 132, 18, 85, $muted)
)
$sb = New-Object Text.StringBuilder
[void]$sb.Append("[Master]`r`nImplementation=2`r`nFontFace=Segoe UI`r`nFontHeight=-16`r`nFontWeight=600`r`nZoomRatio=1`r`n")
[void]$sb.Append("[Settings]`r`nLockUserSettings=1`r`nName=Lotus Lateral`r`nRefreshPeriod=250`r`n")
[void]$sb.Append("[General]`r`nSources=$sourceCount`r`nTables=0`r`nLayers=$($layers.Count)`r`n")
[void]$sb.Append($sources)
for ($i = 0; $i -lt $layers.Count; $i++) {
    $l = $layers[$i]
    [void]$sb.Append("[Layer$i]`r`nName=$($l[0])`r`nText=$($l[1])`r`nPositionX=$($l[2])`r`nPositionY=$($l[3])`r`nExtentX=$($l[4])`r`nExtentY=$($l[5])`r`nExtentOrigin=0`r`nFixedAlignment=1`r`nSize=$($l[6])`r`nTextColor=$($l[7])`r`n")
}
$out = Join-Path $dir 'Lotus-Lateral.ovl'
[IO.File]::WriteAllBytes($out, $latin.GetBytes($sb.ToString()))
Write-Host "Saved $out ($($layers.Count) layers, $sourceCount sources)"
