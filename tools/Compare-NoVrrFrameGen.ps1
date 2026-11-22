param([Parameter(Mandatory=$true)][string]$Output,[string]$Exe='G1R-Win64-Shipping.exe',[int]$Seconds=60,[string]$OriginalPath)
$ErrorActionPreference='Stop'
# Bounded local experiment requested by the user (22/11/2026): frame generation on a fixed-refresh monitor without
# G-Sync/FreeSync. Only this game's RTSS profile is changed; the original bytes are restored at the end.
# Cases: reflex = RTSS Reflex limiter at 74.97; scanline = RTSS scanline sync -40 (tear line in the blanking interval,
# limit off); nolimit = no RTSS limit (plain V-Sync off, shows the tearing baseline).
$Output=[IO.Path]::GetFullPath($Output)
if($Exe -notmatch '^[A-Za-z0-9_\-\.]+\.exe$'){throw 'Invalid exe name'}
$profile="C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\$Exe.cfg"
$pm=Join-Path $env:LOCALAPPDATA 'Lotus\Tools\PresentMon\2.6.0\PresentMon-2.6.0-x64.exe'
if((Get-FileHash -LiteralPath $pm -Algorithm SHA256).Hash -ne 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'){throw 'Unexpected PresentMon binary'}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$backup=Join-Path $Output 'rtss-original.cfg'
if(Test-Path -LiteralPath $backup){throw 'Use a new output folder; original backup already exists'}
$existed=Test-Path -LiteralPath $profile
$original=if($OriginalPath){$existed=$true;[IO.File]::ReadAllBytes($OriginalPath)}elseif($existed){[IO.File]::ReadAllBytes($profile)}else{[byte[]]@()}
[IO.File]::WriteAllBytes($backup,$original)
Add-Type @'
using System.Runtime.InteropServices;
public static class NoVrrRtssReload {
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
# Limit, LimitDenominator, SyncLimiter, SyncScanline0
$cases=@{ 'reflex'=@('7497','100','3','0'); 'reflex60'=@('5999','100','3','0'); 'scanline'=@('0','1','0','-40'); 'nolimit'=@('0','1','0','0') }
$latin=[Text.Encoding]::GetEncoding(28591)
$timer=[Diagnostics.Stopwatch]::StartNew()
$collector=$null
try {
 'ready' | Set-Content -LiteralPath (Join-Path $Output 'helper.ready')
 while($timer.Elapsed.TotalMinutes -lt 45 -and -not (Test-Path -LiteralPath (Join-Path $Output 'stop.req'))) {
  $request=Join-Path $Output 'case.req'
  if(-not (Test-Path -LiteralPath $request)){Start-Sleep -Milliseconds 200;continue}
  $id=(Get-Content -LiteralPath $request -Raw).Trim()
  Remove-Item -LiteralPath $request
  $parts=$id -split '__'
  if($parts.Count -ne 2 -or -not $cases.ContainsKey($parts[0]) -or $parts[1] -notmatch '^[a-z0-9-]{1,24}$'){throw 'Invalid case'}
  $c=$cases[$parts[0]]
  $folder=Join-Path $Output $id
  if(Test-Path -LiteralPath $folder){throw 'Case already measured'}
  New-Item -ItemType Directory -Path $folder | Out-Null
  $ini=$latin.GetString($original)
  $ini=Set-Key $ini 'Framerate' 'Limit' $c[0];$ini=Set-Key $ini 'Framerate' 'LimitDenominator' $c[1]
  $ini=Set-Key $ini 'Framerate' 'SyncLimiter' $c[2];$ini=Set-Key $ini 'Framerate' 'SyncScanline0' $c[3]
  $ini=Set-Key $ini 'Statistics' 'FrametimeCalc' '0'
  [IO.File]::WriteAllText($profile,$ini,$latin)
  [NoVrrRtssReload]::UpdateProfiles()
  [IO.File]::Copy($profile,(Join-Path $folder 'rtss-applied.cfg'))
  Start-Sleep -Seconds 3
  $csv=Join-Path $folder 'presentmon.csv'
  $arguments=@('--process_name',$Exe,'--output_file',('"'+$csv+'"'),'--session_name',('LotusNoVrr-'+[Guid]::NewGuid().ToString('N')),'--timed',"$Seconds",'--terminate_after_timed','--no_console_stats','--track_frame_type','--track_pc_latency','--track_etw_status')
  $collector=Start-Process -FilePath $pm -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $folder 'collector.log') -RedirectStandardError (Join-Path $folder 'collector-errors.log') -PassThru
  $deadline=[DateTime]::UtcNow.AddSeconds($Seconds+20)
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
 if($existed){[IO.File]::WriteAllBytes($profile,$original)}else{Remove-Item -LiteralPath $profile -ErrorAction SilentlyContinue}
 [NoVrrRtssReload]::UpdateProfiles()
 $same=if($existed){[BitConverter]::ToString([IO.File]::ReadAllBytes($profile)) -eq [BitConverter]::ToString($original)}else{-not(Test-Path -LiteralPath $profile)}
 @{Restored=$same;Utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'restored.json')
}
