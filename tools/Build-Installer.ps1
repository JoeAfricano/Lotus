# Builds the Lotus installer: self-contained Release publish + Inno Setup.
# Usage: powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1 [-Name "Lotus Base"]
# Output: artifacts\dist\<Name>-Setup-<version>.exe (+ .sha256). Needs Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Google Drive backup: put the Desktop OAuth client file at secrets\google-oauth.json (not in Git); it is copied into the build.
param([string]$Name = "Lotus")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Publish-Lotus.ps1')
$out = Join-Path $root 'artifacts\dist\Lotus'
$google = Join-Path $root 'secrets\google-oauth.json'
if (Test-Path $google) { Copy-Item $google (Join-Path $out 'google-oauth.json') -Force; Write-Output "Google Drive: client included" }
else { Remove-Item (Join-Path $out 'google-oauth.json') -ErrorAction SilentlyContinue; Write-Output "Google Drive: no client (secrets\google-oauth.json missing) - file backups only" }
$version = (Get-Item (Join-Path $out 'Lotus.App.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$setupName = ($Name -replace '\s+', '-') + '-Setup'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
& $iscc "/DAppVersion=$version" "/DSourceDir=$out" "/DSetupName=$setupName" "/DOutputDir=$(Join-Path $root 'artifacts\dist')" (Join-Path $PSScriptRoot 'installer\Lotus.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
$setup = Join-Path $root "artifacts\dist\$setupName-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Write-Output "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
Write-Output "SHA256:    $hash"
