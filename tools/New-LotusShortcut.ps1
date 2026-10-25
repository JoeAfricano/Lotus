param([string]$Configuration = "Release")
# Creates "Lotus.lnk" in the repository root, pointing at the built app, so it can be opened without any IDE.
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\Lotus.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe"
if (-not (Test-Path $exe)) { throw "Build $Configuration/x64 first: $exe not found." }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $root "Lotus.lnk"))
$link.TargetPath = $exe
$link.WorkingDirectory = Split-Path $exe
$link.IconLocation = (Join-Path $root "src\Lotus.App\Assets\Lotus.ico") + ",0"
$link.Description = "Lotus"
$link.Save()
Write-Output "Shortcut created: $(Join-Path $root 'Lotus.lnk') -> $exe"
