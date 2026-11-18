param(
    [ValidateRange(10,120)][int]$DurationSeconds = 60,
    [string]$OutputDirectory = 'artifacts/ambient-background',
    [ValidateRange(1,5)][int]$Rounds = 1,
    [string]$ExecutablePath
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$defaultExecutable = Join-Path $workspace 'src\Lotus.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe'
$executable = if ($ExecutablePath) { [IO.Path]::GetFullPath($ExecutablePath) } else { $defaultExecutable }
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or [IO.Path]::GetFileName($executable) -ne 'Lotus.App.exe') { throw 'A built Lotus.App.exe is required.' }
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'src\Lotus.App\bin')) + [IO.Path]::DirectorySeparatorChar
if (-not $executable.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Measurement executable must be inside this workspace build output.' }
$preferencesPath = Join-Path $env:LOCALAPPDATA 'Lotus\preferences.json'
$preferencesBefore = if (Test-Path -LiteralPath $preferencesPath) { (Get-FileHash -LiteralPath $preferencesPath).Hash } else { $null }
$outputPath = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
function Close-MeasurementLotus {
    foreach ($target in @(Get-Process Lotus.App -ErrorAction SilentlyContinue)) {
        if ($target.Path -notin @($executable,$defaultExecutable)) { throw 'Another Lotus instance is running; close it before measuring.' }
        if (-not $target.CloseMainWindow()) { throw 'Lotus refused graceful close.' }
        if (-not $target.WaitForExit(10000)) { throw 'Lotus did not close within 10 seconds.' }
    }
}
function Read-SessionEvents([datetimeoffset]$since) {
    Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Lotus\Logs') -Filter 'lotus-*.jsonl' |
        ForEach-Object { Get-Content -LiteralPath $_.FullName } |
        ForEach-Object { try { $_ | ConvertFrom-Json } catch { } } |
        Where-Object { [datetimeoffset]$_.Timestamp -ge $since } | Sort-Object Timestamp
}
function Read-Gpu([int]$targetId) {
    try {
        $dwmIds = @(Get-Process dwm -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
        Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine | Where-Object {
            $gpuName = $_.Name
            $gpuName -like "pid_${targetId}_*" -or @($dwmIds | Where-Object { $gpuName -like "pid_$($_)_*" }).Count -gt 0
        } | Select-Object Name,UtilizationPercentage
    } catch { [pscustomobject]@{Unavailable=$_.Exception.Message} }
}
$results = [Collections.Generic.List[object]]::new()
try {
    Close-MeasurementLotus
    for ($round = 1; $round -le $Rounds; $round++) {
      # Alternate order to reduce a systematic warm-up/order bias.
      $order = if ($round % 2 -eq 0) { @($false,$true) } else { @($true,$false) }
      foreach ($enabled in $order) {
        $started = [datetimeoffset]::Now
        $arguments = if ($enabled) { @('--ambient-motion-proof') } else { @('--ambient-motion-proof','--ambient-motion-disabled') }
        $run = Start-Process -FilePath $executable -ArgumentList $arguments -PassThru
        Start-Sleep -Seconds 8
        $condition = if ($enabled) { 'on' } else { 'off' }; $label = if ($Rounds -eq 1) { "ambient-motion-$condition" } else { "round-$round-motion-$condition" }
        @(Read-Gpu $run.Id) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath "$label-gpu-start.json") -Encoding utf8
        $before = [datetimeoffset]::Now
        $initialPolicy = Read-SessionEvents $started | Where-Object MessageTemplate -like 'Ambient motion*' | Select-Object -Last 1
        & (Join-Path $PSScriptRoot 'Measure-LotusProcess.ps1') -LotusProcessId $run.Id -Label $label -DurationSeconds $DurationSeconds -OutputDirectory $outputPath
        $after = [datetimeoffset]::Now
        @(Read-Gpu $run.Id) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath "$label-gpu-end.json") -Encoding utf8
        Close-MeasurementLotus
        $events = @(Read-SessionEvents $started)
        $events | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath "$label-events.json") -Encoding utf8
        $during = @($events | Where-Object { [datetimeoffset]$_.Timestamp -ge $before -and [datetimeoffset]$_.Timestamp -le $after })
        $policyDuring = @($during | Where-Object MessageTemplate -like 'Ambient motion*')
        $foreground = $null -ne $initialPolicy -and $initialPolicy.Properties.Active -and $initialPolicy.Properties.Visible -and
            @($policyDuring | Where-Object { -not $_.Properties.Active -or -not $_.Properties.Visible }).Count -eq 0
        $motion = $enabled -and $null -ne $initialPolicy -and $initialPolicy.Properties.Running -and
            @($policyDuring | Where-Object { -not $_.Properties.Running }).Count -eq 0
        $stableHome = @($events | Where-Object { $_.MessageTemplate -like 'Navigation to*' -and $_.Properties.SectionId -ne 'home' }).Count -eq 0
        $sensors = @($events | Where-Object { $_.Properties.MonitoringEvent -eq 'Monitoring started' }).Count -gt 0
        $viewport = $events | Where-Object MessageTemplate -like 'Ambient resources created*' | Select-Object -First 1
        $sizeStable = $null -ne $viewport -and $viewport.Properties.Width -gt 0 -and $viewport.Properties.Height -gt 0 -and
            @($during | Where-Object MessageTemplate -like 'Ambient viewport resized*').Count -eq 0
        $static = -not $enabled -and $null -ne $initialPolicy -and -not $initialPolicy.Properties.Running -and
            @($policyDuring | Where-Object { $_.Properties.Running }).Count -eq 0
        $preferencesAfter = if (Test-Path -LiteralPath $preferencesPath) { (Get-FileHash -LiteralPath $preferencesPath).Hash } else { $null }
        $preferencesUnchanged = $preferencesBefore -eq $preferencesAfter
        $valid = $foreground -and $stableHome -and -not $sensors -and $sizeStable -and $preferencesUnchanged -and ($motion -or $static)
        $results.Add([pscustomobject]@{Label=$label;Round=$round;ProcessId=$run.Id;RequestedMotion=$enabled;InitialPolicy=$initialPolicy;
            ViewportWidth=$viewport.Properties.Width;ViewportHeight=$viewport.Properties.Height;SizeStable=$sizeStable;
            StaticThroughout=$static;PreferencesUnchanged=$preferencesUnchanged;ValidConditions=$valid;
            ForegroundThroughout=$foreground;MotionThroughout=$motion;StableHome=$stableHome;SensorsStarted=$sensors;Started=$before.ToString('o');Ended=$after.ToString('o')})
      }
    }
} finally {
    Close-MeasurementLotus
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'comparison-conditions.json') -Encoding utf8
    [pscustomobject]@{Executable=$executable;ExecutableSha256=(Get-FileHash -LiteralPath $executable).Hash;PreferencesSha256Before=$preferencesBefore;DurationPerConditionSeconds=$DurationSeconds;Rounds=$Rounds;WarmupSeconds=8;ExpectedPage='home'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'measurement-manifest.json') -Encoding utf8
    Start-Process -FilePath $executable -PassThru | Select-Object Id,ProcessName | ConvertTo-Json
}
$results | ConvertTo-Json -Depth 6
