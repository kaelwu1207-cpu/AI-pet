param([int]$Runs=3)
$ErrorActionPreference='Stop'
if($Runs -lt 1 -or $Runs -gt 3){throw 'Runs budget 1..3'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));Set-Location -LiteralPath $root
$id=[guid]::NewGuid().ToString('N');$output=Join-Path $root "data/receipts/performance/$id"
New-Item -ItemType Directory -Path $output | Out-Null
$env:DOTNET_CLI_HOME="$root/data/toolchains/dotnet-home";$env:NUGET_PACKAGES="$root/data/cache/nuget"
$env:NUGET_HTTP_CACHE_PATH="$root/data/cache/nuget-http";$env:TEMP="$root/data/temp";$env:TMP=$env:TEMP
$dotnet="$root/data/toolchains/dotnet/dotnet.exe"
$profile=Get-Content contracts/performance-v1.json -Raw|ConvertFrom-Json
$checkpoint=& git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
$sources=@(Get-ChildItem src/host,src/capture-rust,contracts,tests/pet-hosts,tools/loop -Recurse -File |
    Where-Object {$_.Extension -in '.rs','.hlsl','.cs','.csproj','.props','.toml','.lock','.json','.ps1'} |
    ForEach-Object {@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;bytes=$_.Length}})
$steps=[Collections.Generic.List[object]]::new();$results=[Collections.Generic.List[object]]::new();$failure=$null
function Step([string]$name,[string[]]$arguments,[int]$seconds) {
    $p=Start-Process $dotnet -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$output/$name.stdout.log" -RedirectStandardError "$output/$name.stderr.log"
    $timeout=-not $p.WaitForExit($seconds*1000)
    if($timeout){$p.Kill($true);$p.WaitForExit()};$p.Refresh()
    $steps.Add(@{name=$name;processId=$p.Id;exitCode=$p.ExitCode;timedOut=$timeout;exited=$p.HasExited})
    if($timeout -or $p.ExitCode -ne 0){throw "$name failed: exit=$($p.ExitCode), timeout=$timeout"}
}
try {
    Step 'build' @('build','tests/pet-hosts/full-screen.csproj','-c','Release','-o',"$root/data/build/full-screen",'-warnaserror','-m:1','-nr:false','-p:UseSharedCompilation=false') 40
    for($index=1;$index -le $Runs;$index++) {
        Step "run-$index" @("$root/data/build/full-screen/full-screen.dll","$output/run-$index",'64') 20
        $result=Get-Content "$output/run-$index/performance.json" -Raw|ConvertFrom-Json
        $violations=[Collections.Generic.List[string]]::new()
        foreach($metric in 'AnalysisP95Ms','GpuP95Ms','MapP95Ms','SelectionP95Ms','FrameGapP95Ms','SourcePrivatePeakBytes','HostPrivatePeakBytes','OwnedGpuBytes','StatisticReadbackBytesPerFrame') {
            if($result.$metric -gt $profile.$metric){$violations.Add($metric)}
        }
        if($result.SourceCpuOneCorePercent/$result.LogicalProcessors -gt $profile.SourceCpuMachinePercent){$violations.Add('SourceCpuMachinePercent')}
        if($result.HostCpuOneCorePercent/$result.LogicalProcessors -gt $profile.HostCpuMachinePercent){$violations.Add('HostCpuMachinePercent')}
        if($result.Frames -lt $profile.MinimumFrames -or $result.StaleSamples -gt $profile.MaximumStaleSamples){$violations.Add('Freshness')}
        $reference=$result.Roi.Area -eq $profile.ReferencePixels -and $result.Adapter -eq 'NVIDIA GeForce RTX 5090' -and $result.LogicalProcessors -eq 32
        $results.Add(@{run=$index;referenceHardwareMatched=$reference;violations=$violations.ToArray();result=$result})
        Write-Output "run=$index frames=$($result.Frames) gpuP95=$($result.GpuP95Ms) analysisP95=$($result.AnalysisP95Ms) violations=$($violations.Count)"
        if(-not $reference -or $violations.Count){throw 'Reference performance budget failed'}
    }
} catch {$failure=$_.ToString();Write-Output $failure}
finally {
    $same=@($sources|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256}).Count -eq 0
    @{schema='aipet.performance-verification/1';runId=$id;nonce=[guid]::NewGuid().ToString('N');checkpoint=$checkpoint;sourceFiles=$sources;sourceUnchanged=$same;
        steps=$steps.ToArray();results=$results.ToArray();profile=$profile;error=$failure;physicalDisplaysExercised=1;pixelReadback=$false;pixelsPersisted=$false;
        completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 12|Set-Content "$output/receipt.json" -Encoding UTF8
    Write-Output "Receipt: $output/receipt.json"
}
if($failure -or -not $same){exit 1};exit 0
