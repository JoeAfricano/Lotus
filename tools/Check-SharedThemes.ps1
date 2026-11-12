<#
.SYNOPSIS
  Confere se o motor de temas copiado no Lotus Ports continua igual ao do Lotus.
.DESCRIPTION
  O Lotus é a fonte oficial de src/Lotus.Core/Themes (ThemeColor, ThemeGallery, ThemeSpec). O Lotus Ports guarda uma
  cópia em src/Lotus.Design/Themes que só difere no namespace (Lotus.Design.Themes) e numa linha de comentário.
  Este script ignora essas duas diferenças e falha (código 1) se houver qualquer outra. Somente leitura: não altera
  nenhum dos dois repositórios. ThemePalette não entra: cada app tem a sua (recursos XAML diferentes).
#>
param([string]$PortsRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) '..\LotusPorts'))

$lotusThemes = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Lotus.Core\Themes'
$portsThemes = Join-Path $PortsRoot 'src\Lotus.Design\Themes'
if (-not (Test-Path $portsThemes)) { Write-Host "Lotus Ports não encontrado em $portsThemes; nada a conferir."; exit 0 }

function Normalize([string]$path) {
    Get-Content $path | Where-Object { $_ -notmatch '^namespace Lotus\.(Core|Design)\.Themes;' -and $_ -notmatch '^// Same engine as Lotus' }
}

$diverged = 0
foreach ($name in 'ThemeColor.cs', 'ThemeGallery.cs', 'ThemeSpec.cs') {
    $copy = Join-Path $portsThemes $name
    if (-not (Test-Path $copy)) { Write-Host "AUSENTE  $name no Lotus Ports"; $diverged++; continue }
    $diff = Compare-Object (Normalize (Join-Path $lotusThemes $name)) (Normalize $copy)
    if ($diff) {
        $diverged++
        Write-Host "DIFERENTE $name ($($diff.Count) linhas; <= Lotus, => Lotus Ports)"
        $diff | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.SideIndicator) $($_.InputObject.Trim())" }
    } else { Write-Host "IGUAL    $name" }
}
if ($diverged) { Write-Host "Atualize a cópia do Lotus Ports a partir do Lotus (fonte oficial)."; exit 1 }
