# Builds a self-contained Lotus package (runs on PCs without .NET installed) and zips it.
# Usage: powershell -ExecutionPolicy Bypass -File tools\Publish-Lotus.ps1
# Output: artifacts\dist\Lotus\ (the app) and artifacts\dist\Lotus-<version>-win-x64.zip.
# The player extracts the zip, opens Lotus.App.exe and uses Settings > Advanced > "Install Lotus on this PC".
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\dist\Lotus'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\Lotus.App\Lotus.App.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
$version = (Get-Item (Join-Path $out 'Lotus.App.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $root "artifacts\dist\Lotus-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Output "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
Write-Output "SHA256:  $hash"
