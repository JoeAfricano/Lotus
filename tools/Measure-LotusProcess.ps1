param(
    [Parameter(Mandatory)][int]$LotusProcessId,
    [Parameter(Mandatory)][string]$Label,
    [ValidateRange(10,300)][int]$DurationSeconds = 60,
    [string]$OutputDirectory = 'artifacts/etapa02'
)
$ErrorActionPreference = 'Stop'
$logical = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
$target = Get-Process -Id $LotusProcessId
if ($target.ProcessName -ne 'Lotus.App') { throw 'Target must be Lotus.App.' }
$executable = $target.Path
$started = Get-Date
$initialCpu = $target.TotalProcessorTime.TotalSeconds
$clock = [Diagnostics.Stopwatch]::StartNew()
$previousCpu = $initialCpu
$previousTime = 0.0
$samples = [Collections.Generic.List[object]]::new()
while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Milliseconds 1000
    $target.Refresh()
    if ($target.HasExited) { throw 'Lotus exited during measurement.' }
    $elapsed = $clock.Elapsed.TotalSeconds
    $cpu = $target.TotalProcessorTime.TotalSeconds
    $samples.Add([pscustomobject]@{
        ElapsedSeconds = $elapsed
        CpuPercent = 100 * ($cpu - $previousCpu) / (($elapsed - $previousTime) * $logical)
        WorkingSetMiB = $target.WorkingSet64 / 1MB
        PrivateCommitMiB = $target.PrivateMemorySize64 / 1MB
        Threads = $target.Threads.Count
        Handles = $target.HandleCount
    })
    $previousCpu = $cpu; $previousTime = $elapsed
}
$summary = [pscustomobject]@{
    Label = $Label; ProcessId = $LotusProcessId; Executable = $executable
    Started = $started.ToString('o'); Ended = (Get-Date).ToString('o')
    LogicalProcessors = $logical; DurationSeconds = $previousTime; Samples = $samples.Count
    CpuMeanPercent = 100 * ($previousCpu - $initialCpu) / ($previousTime * $logical)
    CpuPeakIntervalPercent = ($samples | Measure-Object CpuPercent -Maximum).Maximum
    WorkingSetMeanMiB = ($samples | Measure-Object WorkingSetMiB -Average).Average
    WorkingSetMinMiB = ($samples | Measure-Object WorkingSetMiB -Minimum).Minimum
    WorkingSetMaxMiB = ($samples | Measure-Object WorkingSetMiB -Maximum).Maximum
    PrivateCommitMeanMiB = ($samples | Measure-Object PrivateCommitMiB -Average).Average
    ThreadsStart = $samples[0].Threads; ThreadsEnd = $samples[-1].Threads
    HandlesStart = $samples[0].Handles; HandlesEnd = $samples[-1].Handles
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$samples | Export-Csv -LiteralPath (Join-Path $OutputDirectory "$Label-samples.csv") -NoTypeInformation -Encoding utf8
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Label-summary.json") -Encoding utf8
$summary | ConvertTo-Json
