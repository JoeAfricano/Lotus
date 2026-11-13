# Rebuilds data\fps-reference.json from tools\fps-reference.csv (the curated table: one row per game).
# Edit the CSV with the values read from the sources listed below, run this script, review the diff, commit at release.
# Columns: name;steamAppId;store;low;medium;high;ultra;gpuHigh;cpuCap;cap;confidence;sources
#   low..ultra  1080p FPS measured by a lab on an RTX 5060 (+ Ryzen 7 9800X3D), when available
#   gpuHigh     1080p High FPS on the calibration PC (Ryzen 5 5500 + RTX 5060) when there is no per-preset lab data
#   cpuCap      FPS the Ryzen 5 5500 class can feed in that game (empty: 62% of the lab "low" value, documented estimate)
#   cap         the game's own frame-rate limit, if any
#   confidence  alta (2+ labs within 15%) | media (one lab) | baixa (Lotus initial table or Steam requirements)
param([string]$Date = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rows = Import-Csv -Path (Join-Path $PSScriptRoot 'fps-reference.csv') -Delimiter ';' -Encoding UTF8
function Num($v) { if ([string]::IsNullOrWhiteSpace($v)) { $null } else { [double]$v } }
$games = foreach ($r in $rows) {
    $lab = @((Num $r.low), (Num $r.medium), (Num $r.high), (Num $r.ultra))
    $g = [ordered]@{ name = $r.name; store = $r.store; confidence = $r.confidence; sources = @($r.sources -split ',') }
    if ($r.steamAppId) { $g.steamAppId = $r.steamAppId }
    if ($lab[0] -ne $null) { $g.lab1080 = @($lab | ForEach-Object { if ($_ -eq $null) { 0 } else { $_ } }) }
    if ($r.gpuHigh) { $g.gpuHigh = Num $r.gpuHigh }
    if ($r.cpuCap) { $g.cpuCap = Num $r.cpuCap }
    if ($r.cap) { $g.cap = [int]$r.cap }
    $g
}
$doc = [ordered]@{
    schema = 1
    date = $Date
    calibration = 'Ryzen 5 5500 + RTX 5060 (1080p)'
    sources = [ordered]@{
        'notebookcheck-5060' = [ordered]@{ label = 'Notebookcheck: GeForce RTX 5060 (desktop), 1080p'; url = 'https://www.notebookcheck.net/GeForce-RTX-5060-vs-GeForce-RTX-5070_12954_12952.247598.0.html' }
        'lotus-initial' = [ordered]@{ label = 'Tabela inicial do Lotus (médias públicas de benchmarks)'; url = 'https://github.com/JoeAfricano/Lotus/blob/main/docs/LOTUS_FPS_SOURCES.md' }
        'steam-requirements' = [ordered]@{ label = 'Requisitos recomendados da Steam (estimativa)'; url = 'https://store.steampowered.com/' }
    }
    games = @($games)
}
$out = Join-Path $root 'data\fps-reference.json'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IO.File]::WriteAllText($out, ($doc | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
Write-Host "Saved $out ($(@($games).Count) games)"
