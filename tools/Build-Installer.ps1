# Builds the Lotus installer: self-contained Release publish + Inno Setup.
# Usage: powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1
# Output: artifacts\dist\Lotus-Setup-<version>.exe (+ .sha256). Needs Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Publish-Lotus.ps1')
$out = Join-Path $root 'artifacts\dist\Lotus'
$version = (Get-Item (Join-Path $out 'Lotus.App.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
& $iscc "/DAppVersion=$version" "/DSourceDir=$out" "/DOutputDir=$(Join-Path $root 'artifacts\dist')" (Join-Path $PSScriptRoot 'installer\Lotus.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
$setup = Join-Path $root "artifacts\dist\Lotus-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Write-Output "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
Write-Output "SHA256:    $hash"
