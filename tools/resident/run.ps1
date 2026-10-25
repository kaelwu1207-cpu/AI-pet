param([ValidateRange(15,28800)][int]$Seconds=600,[string]$Skin='green',[string]$Motion='wander',[switch]$ExercisePause)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));Set-Location -LiteralPath $root
$id=[guid]::NewGuid().ToString('N');$output="$root/data/receipts/resident/$id"
New-Item -ItemType Directory -Path $output|Out-Null
$checkpoint=& git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
$paths=@(Get-ChildItem src/host,src/capture-rust,assets,contracts,tools/resident -Recurse -File|Where-Object{$_.Extension -in '.cs','.csproj','.rs','.hlsl','.toml','.lock','.json','.png','.ps1'})
$sources=@($paths|ForEach-Object{@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
@{AssetsRoot="$root/assets";Skin=$Skin;Motion=$Motion;ActionIntervalMs=5000}|ConvertTo-Json|Set-Content "$output/config.json" -Encoding utf8
$start=[Diagnostics.ProcessStartInfo]::new("$root/data/toolchains/dotnet/dotnet.exe")
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$root
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
foreach($arg in @("$root/data/build/resident/AIpet.Host.dll",'--resident',"$output/config.json","$root/data/build/capture-rust/release/observe.exe","$output/run")){$start.ArgumentList.Add($arg)}
$p=[Diagnostics.Process]::Start($start);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
$watch=[Diagnostics.Stopwatch]::StartNew();$paused=$false;$resumed=$false;$timeout=$false;$failure=$null;$controls=[Collections.Generic.List[object]]::new()
$profile=Get-Content contracts/resident-v1.json -Raw|ConvertFrom-Json
$baseline=[Collections.Generic.List[object]]::new();$lastBaselineUtc=''
Write-Output "RUN_ID=$id HOST_PID=$($p.Id) OUTPUT=$output"
try{
    while($watch.Elapsed.TotalSeconds -lt $Seconds){
        if($p.HasExited){throw "Resident exited early: $($p.ExitCode)"}
        if($ExercisePause -and -not $paused -and $watch.Elapsed.TotalSeconds -gt 10){$p.StandardInput.WriteLine('pause');$paused=$true;$controls.Add(@{command='pause';seconds=$watch.Elapsed.TotalSeconds})}
        if($ExercisePause -and $paused -and -not $resumed -and $watch.Elapsed.TotalSeconds -gt 13){$p.StandardInput.WriteLine('resume');$resumed=$true;$controls.Add(@{command='resume';seconds=$watch.Elapsed.TotalSeconds})}
        $state=$null
        if(Test-Path "$output/run/status.json"){try{$state=Get-Content "$output/run/status.json" -Raw|ConvertFrom-Json}catch{}}
        if($state -and $state.State -eq 5){throw "Resident faulted: $($state.Error)"}
        if($watch.Elapsed.TotalSeconds -ge $profile.WarmupSeconds -and $watch.Elapsed.TotalSeconds -lt ($profile.WarmupSeconds+30) -and (Test-Path "$output/run/metrics.jsonl")){
            $line=$null;try{$line=Get-Content "$output/run/metrics.jsonl" -Tail 1|ConvertFrom-Json}catch{}
            if($line -and $line.Utc -ne $lastBaselineUtc){$baseline.Add($line);$lastBaselineUtc=$line.Utc}
        }
        Start-Sleep -Milliseconds 500
    }
}catch{$failure=$_.ToString()}
finally{
    if(-not $p.HasExited){$p.StandardInput.WriteLine('exit');$p.StandardInput.Close();if(-not $p.WaitForExit(10000)){$timeout=$true;$p.Kill();$p.WaitForExit()}}
    $stdout.GetAwaiter().GetResult()|Set-Content "$output/stdout.log";$stderr.GetAwaiter().GetResult()|Set-Content "$output/stderr.log"
    $p.Refresh();$same=@($sources|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256}).Count -eq 0
    $end=$null;if(Test-Path "$output/run/resident-result.json"){$end=Get-Content "$output/run/resident-result.json" -Raw|ConvertFrom-Json}
    $samples=@();foreach($file in @("$output/run/metrics.jsonl.previous","$output/run/metrics.jsonl")){if(Test-Path $file){$samples+=@(Get-Content $file|ForEach-Object{$_|ConvertFrom-Json})}}
    $warm=@($samples|Where-Object{([datetime]$_.Utc-[datetime]$samples[0].Utc).TotalSeconds -ge 15})
    function Peak($field){if($samples.Count){($samples|Measure-Object -Property $field -Maximum).Maximum}else{0}}
    function Median($items,$field){$values=@($items|ForEach-Object{[double]$_.$field}|Sort-Object);if($values.Count){$values[[int][Math]::Floor($values.Count/2)]}else{$null}}
    $tail=@($samples|Select-Object -Last 30);$trend=@{}
    foreach($field in 'HostPrivateBytes','SourcePrivateBytes','HostHandles','SourceHandles'){
        $trend[$field]=if($baseline.Count -and $tail.Count){(Median $tail $field)-(Median $baseline $field)}else{$null}
    }
    $measured=$watch.Elapsed.TotalSeconds
    $summary=@{schema='aipet.resident-soak/1';runId=$id;nonce=[guid]::NewGuid().ToString('N');checkpoint=$checkpoint;sourceFiles=$sources;sourceUnchanged=$same;
        hostPid=$p.Id;exitCode=$p.ExitCode;forcedTermination=$timeout;requestedSeconds=$Seconds;actualSeconds=$measured;controls=$controls.ToArray();
        logicalProcessors=[Environment]::ProcessorCount;metricsSamples=$samples.Count;end=$end;error=$failure;pixelsPersisted=$false;
        hostPrivateSamplePeakBytes=(Peak 'HostPrivateBytes');sourcePrivateSamplePeakBytes=(Peak 'SourcePrivateBytes');hostHandlesSamplePeak=(Peak 'HostHandles');sourceHandlesSamplePeak=(Peak 'SourceHandles');
        fullLifetimeCpuMachinePercent=if($end){($end.HostCpuMs+$end.CaptureCpuMs+$end.EffectCpuMs)/($measured*1000*[Environment]::ProcessorCount)*100}else{$null};
        warmSamples=$warm.Count;staleWarmSamples=@($warm|Where-Object{-not $_.Fresh}).Count;baselineSamples=$baseline.Count;medianGrowth=$trend;
        profile=$profile;completedUtc=[DateTime]::UtcNow.ToString('o')}
    $violations=[Collections.Generic.List[string]]::new()
    if($summary.fullLifetimeCpuMachinePercent -gt $profile.CpuMachinePercent){$violations.Add('CpuMachinePercent')}
    if(($summary.hostPrivateSamplePeakBytes+$summary.sourcePrivateSamplePeakBytes+$end.EffectPrivatePeakBytes) -gt $profile.PrivateComponentPeaksSumBytes){$violations.Add('PrivateComponentPeaksSumBytes')}
    if($Seconds -ge $profile.ReferenceMeasurementSeconds){
        if($baseline.Count -lt 10){$violations.Add('InsufficientBaseline')}
        if($trend.HostPrivateBytes -gt $profile.HostPrivateGrowthBytes){$violations.Add('HostPrivateGrowth')}
        if($trend.SourcePrivateBytes -gt $profile.SourcePrivateGrowthBytes){$violations.Add('SourcePrivateGrowth')}
        if($trend.HostHandles -gt $profile.HostHandleGrowth){$violations.Add('HostHandleGrowth')}
        if($trend.SourceHandles -gt $profile.SourceHandleGrowth){$violations.Add('SourceHandleGrowth')}
    }
    $summary.budgetViolations=$violations.ToArray();$summary.longStabilityDurationReached=$Seconds -ge $profile.LongStabilitySeconds -and $measured -ge $profile.LongStabilitySeconds
    $summary|ConvertTo-Json -Depth 12|Set-Content "$output/receipt.json" -Encoding utf8
    Write-Output "RECEIPT=$output/receipt.json EXIT=$($p.ExitCode) SECONDS=$measured ERROR=$failure"
}
if($failure -or $timeout -or $p.ExitCode -ne 0 -or -not $same -or $violations.Count){exit 1};exit 0
