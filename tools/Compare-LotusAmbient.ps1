param(
    [ValidateRange(10,120)][int]$DurationSeconds = 60,
    [string]$OutputDirectory = 'artifacts/ambient-background'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $workspace 'src\Lotus.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe'
$outputPath = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
function Close-MeasurementLotus {
    foreach ($target in @(Get-Process Lotus.App -ErrorAction SilentlyContinue)) {
        if ($target.Path -ne $executable) { continue }
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
    foreach ($enabled in @($true,$false)) {
        $started = [datetimeoffset]::Now
        $arguments = if ($enabled) { @('--ambient-motion-proof') } else { @('--ambient-motion-proof','--ambient-motion-disabled') }
        $run = Start-Process -FilePath $executable -ArgumentList $arguments -PassThru
        Start-Sleep -Seconds 8
        $label = if ($enabled) { 'ambient-motion-on' } else { 'ambient-motion-off' }
        @(Read-Gpu $run.Id) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath "$label-gpu-start.json") -Encoding utf8
        $before = [datetimeoffset]::Now
        $initialPolicy = Read-SessionEvents $started | Where-Object MessageTemplate -like 'Ambient motion*' | Select-Object -Last 1
        & (Join-Path $PSScriptRoot 'Measure-LotusProcess.ps1') -LotusProcessId $run.Id -Label $label -DurationSeconds $DurationSeconds -OutputDirectory $outputPath
        $after = [datetimeoffset]::Now
        @(Read-Gpu $run.Id) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath "$label-gpu-end.json") -Encoding utf8
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
        $results.Add([pscustomobject]@{Label=$label;ProcessId=$run.Id;RequestedMotion=$enabled;InitialPolicy=$initialPolicy;
            ForegroundThroughout=$foreground;MotionThroughout=$motion;StableHome=$stableHome;SensorsStarted=$sensors;Started=$before.ToString('o');Ended=$after.ToString('o')})
        Close-MeasurementLotus
    }
} finally {
    Close-MeasurementLotus
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'comparison-conditions.json') -Encoding utf8
    Start-Process -FilePath $executable -PassThru | Select-Object Id,ProcessName | ConvertTo-Json
}
$results | ConvertTo-Json -Depth 6
