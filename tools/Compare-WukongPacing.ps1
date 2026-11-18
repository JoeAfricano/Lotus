param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
# Bounded local experiment, explicitly requested by the user. Only Wukong's RTSS profile is changed.
$Output=[IO.Path]::GetFullPath($Output)
$profile='C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\b1-Win64-Shipping.exe.cfg'
$pm=Join-Path $env:LOCALAPPDATA 'Lotus\Tools\PresentMon\2.6.0\PresentMon-2.6.0-x64.exe'
if((Get-FileHash -LiteralPath $pm -Algorithm SHA256).Hash -ne 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'){throw 'Unexpected PresentMon binary'}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$backup=Join-Path $Output 'rtss-original.cfg'
if(Test-Path -LiteralPath $backup){throw 'Use a new output folder; original backup already exists'}
$original=[IO.File]::ReadAllBytes($profile)
[IO.File]::WriteAllBytes($backup,$original)
Add-Type @'
using System.Runtime.InteropServices;
public static class WukongRtssReload {
 [DllImport("C:\\Program Files (x86)\\RivaTuner Statistics Server\\RTSSHooks64.dll",CallingConvention=CallingConvention.Cdecl)]
 public static extern void UpdateProfiles();
}
'@
function Set-Key([string]$Text,[string]$Section,[string]$Key,[string]$Value) {
 $pattern='(?ms)^\['+[regex]::Escape($Section)+'\][^\r\n]*\r?\n(?<body>.*?)(?=^\[|\z)'
 $match=[regex]::Match($Text,$pattern)
 if(-not $match.Success){return $Text.TrimEnd()+"`r`n[$Section]`r`n$Key=$Value`r`n"}
 $body=$match.Groups['body'].Value
 $keyPattern='(?m)^'+[regex]::Escape($Key)+'\s*=.*$'
 if([regex]::IsMatch($body,$keyPattern)){$body=[regex]::Replace($body,$keyPattern,($Key+'='+$Value))}
 else{$body=$body.TrimEnd()+"`r`n$Key=$Value`r`n"}
 return $Text.Substring(0,$match.Index)+"[$Section]`r`n"+$body+$Text.Substring($match.Index+$match.Length)
}
$cases=@{
 'front-passive'=@(1,1,1); 'front-active'=@(1,0,1);
 'back-active'=@(2,0,0); 'reflex'=@(3,1,0); 'async-active'=@(0,0,0)
}
$timer=[Diagnostics.Stopwatch]::StartNew()
$collector=$null
try {
 'ready' | Set-Content -LiteralPath (Join-Path $Output 'helper.ready')
 while($timer.Elapsed.TotalMinutes -lt 30 -and -not (Test-Path -LiteralPath (Join-Path $Output 'stop.req'))) {
  $request=Join-Path $Output 'case.req'
  if(-not (Test-Path -LiteralPath $request)){Start-Sleep -Milliseconds 200;continue}
  $id=(Get-Content -LiteralPath $request -Raw).Trim()
  Remove-Item -LiteralPath $request
  $parts=$id -split '__'
  if($parts.Count -ne 2 -or -not $cases.ContainsKey($parts[0]) -or $parts[1] -notmatch '^[1-3]$'){throw 'Invalid case'}
  $selected=$cases[$parts[0]]
  $folder=Join-Path $Output $id
  if(Test-Path -LiteralPath $folder){throw 'Case already measured'}
  New-Item -ItemType Directory -Path $folder | Out-Null
  $ini=[Text.Encoding]::Latin1.GetString($original)
  # Preserve 75 Hz baseline from the earlier Wukong study: 74.97 FPS. Do not change Windows or game settings.
  $ini=Set-Key $ini 'Framerate' 'Limit' '7497';$ini=Set-Key $ini 'Framerate' 'LimitDenominator' '100'
  $ini=Set-Key $ini 'Framerate' 'SyncLimiter' ([string]$selected[0]);$ini=Set-Key $ini 'Framerate' 'SyncScanline0' '0'
  $ini=Set-Key $ini 'Framerate' 'PassiveWait' ([string]$selected[1]);$ini=Set-Key $ini 'Statistics' 'FrametimeCalc' ([string]$selected[2])
  [IO.File]::WriteAllText($profile,$ini,[Text.Encoding]::Latin1)
  [WukongRtssReload]::UpdateProfiles()
  [IO.File]::Copy($profile,(Join-Path $folder 'rtss-applied.cfg'))
  @{Case=$id;Cap=74.97;SyncLimiter=$selected[0];PassiveWait=$selected[1];FrametimeCalc=$selected[2];Utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'conditions.json')
  'ready' | Set-Content -LiteralPath (Join-Path $folder 'ready')
  $go=Join-Path $folder 'go'
  while(-not(Test-Path -LiteralPath $go)) {
   if($timer.Elapsed.TotalMinutes -ge 30 -or (Test-Path -LiteralPath (Join-Path $Output 'stop.req'))){return}
   Start-Sleep -Milliseconds 100
  }
  $csv=Join-Path $folder 'presentmon.csv'
  $arguments=@('--process_name','b1-Win64-Shipping.exe','--output_file',('"'+$csv+'"'),'--session_name',('LotusPacing-'+[Guid]::NewGuid().ToString('N')),'--v1_metrics','--timed','185','--terminate_after_timed','--no_console_stats','--no_track_input','--track_frame_type','--track_etw_status')
  $collector=Start-Process -FilePath $pm -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $folder 'collector.log') -RedirectStandardError (Join-Path $folder 'collector-errors.log') -PassThru
  @{Pid=$collector.Id;Utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'capture.started')
  $deadline=[DateTime]::UtcNow.AddSeconds(205)
  while(-not $collector.HasExited) {
   if([DateTime]::UtcNow -gt $deadline -or (Test-Path -LiteralPath (Join-Path $Output 'stop.req'))){$collector.Kill();break}
   Start-Sleep -Milliseconds 200
  }
  $collector.WaitForExit()
  @{ExitCode=$collector.ExitCode;Utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'complete.json')
  $collector.Dispose();$collector=$null
 }
} catch {
 $_ | Out-String | Set-Content -LiteralPath (Join-Path $Output 'failure.txt')
 throw
} finally {
 if($collector -and -not $collector.HasExited){$collector.Kill();$collector.Dispose()}
 [IO.File]::WriteAllBytes($profile,$original)
 [WukongRtssReload]::UpdateProfiles()
 $same=[Convert]::ToHexString([IO.File]::ReadAllBytes($profile)) -eq [Convert]::ToHexString($original)
 @{Restored=$same;Utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'restored.json')
}
