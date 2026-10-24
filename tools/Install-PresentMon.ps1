param()
$ErrorActionPreference = 'Stop'
# Pinned official release. No installer, service, driver or machine-wide configuration.
$version = '2.6.0'
$expected = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
$directory = Join-Path $env:LOCALAPPDATA "Lotus/Tools/PresentMon/$version"
New-Item -ItemType Directory -Force $directory | Out-Null
$destination = Join-Path $directory "PresentMon-$version-x64.exe"
if (!(Test-Path -LiteralPath $destination)) {
    Invoke-WebRequest "https://github.com/GameTechDev/PresentMon/releases/download/v$version/PresentMon-$version-x64.exe" -OutFile "$destination.download"
    if ((Get-FileHash -LiteralPath "$destination.download" -Algorithm SHA256).Hash -ne $expected) { throw 'PresentMon SHA-256 mismatch. Download was not installed.' }
    Move-Item -LiteralPath "$destination.download" -Destination $destination
}
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) { throw 'PresentMon SHA-256 mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $destination
if ($signature.Status -ne 'Valid') { throw "PresentMon signature validation failed: $($signature.Status)" }
foreach ($name in @('LICENSE.txt','THIRD_PARTY.txt')) {
    Invoke-WebRequest "https://raw.githubusercontent.com/GameTechDev/PresentMon/v$version/$name" -OutFile (Join-Path $directory $name)
}
Write-Output "Verified PresentMon ${version}: $destination"
