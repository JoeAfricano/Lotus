# Builds a self-contained Lotus package (runs on PCs without .NET installed) and zips it.
# Usage: powershell -ExecutionPolicy Bypass -File tools\Publish-Lotus.ps1
# Output: artifacts\dist\Lotus\ (the app) and artifacts\dist\Lotus-<version>-win-x64.zip.
# The player extracts the zip, opens Lotus.App.exe and uses Settings > Advanced > "Install Lotus on this PC".
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\dist\Lotus'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
# No debug symbols, and only the languages Lotus speaks (pt-BR, en-US) for the framework's own resources.
dotnet publish (Join-Path $root 'src\Lotus.App\Lotus.App.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true `
    -p:DebugType=none -p:DebugSymbols=false "-p:SatelliteResourceLanguages=en-US%3Bpt-BR" -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
# Compact install folder: WinUI's built-in texts only in Lotus' languages (others fall back to English), and no
# developer-only tools (crash dump writer, debug symbol reader). Nothing the player uses depends on these.
$keepLanguages = @('en-US', 'pt-BR')
Get-ChildItem $out -Directory | Where-Object { $_.Name -match '^[a-z]{2,3}(-[A-Za-z]{2,4})*(-[A-Za-z0-9]{2,8})?$' -and $keepLanguages -notcontains $_.Name -and
    (Get-ChildItem $_.FullName -Recurse -File | Where-Object { $_.Extension -notin '.mui', '.dll' }).Count -eq 0 } | Remove-Item -Recurse -Force
foreach ($dev in 'createdump.exe', 'Microsoft.DiaSymReader.Native.amd64.dll') { Remove-Item (Join-Path $out $dev) -Force -ErrorAction SilentlyContinue }
Get-ChildItem $out -Recurse -Include *.pdb, *.xml -File | Where-Object { $_.Name -notlike '*.deps.*' } | Remove-Item -Force
$version = (Get-Item (Join-Path $out 'Lotus.App.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $root "artifacts\dist\Lotus-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Output "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
Write-Output "SHA256:  $hash"
