<#
.SYNOPSIS
  Screen tests of Lotus (FlaUI): opens a development instance of the already built Lotus, walks every
  section of the side menu, checks each one and saves a proof print per section.
.DESCRIPTION
  - Uses the Debug x64 build (build it first) or the exe given in -App.
  - Never builds Lotus.App, never presses action buttons, never changes Windows.
  - preferences.json is copied before and put back after the run.
  - Prints go to artifacts\ui-tests\<date_time>\ (01-home.png, 02-optimize.png, ...).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Run-UiTests.ps1
#>
param([string]$App = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $root ("artifacts\ui-tests\" + (Get-Date -Format "yyyy-MM-dd_HHmmss"))
$env:LOTUS_UI_TESTS = "1"
$env:LOTUS_UI_SHOTS = $shots
if ($App) { $env:LOTUS_UI_APP = (Resolve-Path $App).Path }
try {
    dotnet test (Join-Path $root "tests\Lotus.UITests") -c Debug -p:Platform=x64 --logger "console;verbosity=normal" --logger "trx;LogFileName=$shots\ui-tests.trx"
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:LOTUS_UI_TESTS, Env:LOTUS_UI_SHOTS, Env:LOTUS_UI_APP -ErrorAction SilentlyContinue
}
Write-Host ""
Write-Host "Prints: $shots"
exit $code
