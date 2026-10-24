$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $root
$runId=[guid]::NewGuid().ToString('N')
$output=Join-Path $root "data\receipts\host\$runId"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $root 'data\toolchains\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root 'data\cache\nuget'
$env:NUGET_HTTP_CACHE_PATH=Join-Path $root 'data\cache\nuget-http'
$env:TEMP=Join-Path $root 'data\temp'
$env:TMP=$env:TEMP
$dotnet=Join-Path $root 'data\toolchains\dotnet\dotnet.exe'
$checkpoint=& git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
if($LASTEXITCODE -ne 0) { throw 'Missing source checkpoint' }
$upstream=& git -c "safe.directory=$($root.Replace('\','/'))/data/upstream/vpet" -C data/upstream/vpet rev-parse HEAD
if($LASTEXITCODE -ne 0 -or $upstream -ne '0c2e795b8e81485e448675316ba088d5226d908d') { throw 'VPet checkpoint mismatch' }
$dirty=& git -c "safe.directory=$($root.Replace('\','/'))/data/upstream/vpet" -C data/upstream/vpet diff --name-only
if($dirty) { throw 'Tracked VPet source changed' }
$sources=@(Get-ChildItem src/host,tests/pet-hosts,tools/host -Recurse -File | ForEach-Object {
    @{path=$_.FullName;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
$steps=[Collections.Generic.List[object]]::new()
function Run-Step([string]$name,[string[]]$arguments,[int]$timeout) {
    $started=[DateTime]::UtcNow
    $p=Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$output/$name.stdout.log" -RedirectStandardError "$output/$name.stderr.log"
    $timedOut=-not $p.WaitForExit($timeout*1000)
    if($timedOut) { $p.Kill(); $p.WaitForExit() }
    $p.Refresh()
    $steps.Add(@{name=$name;exitCode=$p.ExitCode;timedOut=$timedOut;processExited=$p.HasExited;processId=$p.Id;
        startedUtc=$started.ToString('o');completedUtc=[DateTime]::UtcNow.ToString('o')})
    Write-Output "$name exit=$($p.ExitCode) timeout=$timedOut"
    if($p.ExitCode -ne 0 -or $timedOut) { throw "$name failed" }
}
$failure=$null
try {
    $build=Join-Path $root 'data\build\host-integration'
    Run-Step 'build' @('build','tests/pet-hosts/host-integration.csproj','-c','Release','-o',$build,'-warnaserror') 60
    Run-Step 'integration' @("$build/host-integration.dll","$output/fixtures") 30
    Run-Step 'display' @("$build/AIpet.Host.dll",'--describe-display') 5
    $scope=Get-Content "$output/display.stdout.log" -Raw | ConvertFrom-Json
    $plan=@{Display=$scope;PetBounds=@{X=300;Y=150;Width=120;Height=120};
        Skin=@{Id='fixture-red';Poses=@{idle="$output/fixtures/red.png"}};
        Animation=@{Id='fixture-idle';Steps=@(@{Pose='idle';DurationMs=150})};
        Motion=@{PlanId='run-directed';DisplayId='session-primary';TopologyEpoch=1;X=500;Y=250;PixelsPerSecond=500;DeadlineMs=2500};
        Effect=@{PlanId='run-blackout';Display=$scope;Bounds=@{X=90;Y=90;Width=120;Height=80};DurationMs=1500;EffectId='blackout'}}
    $plan | ConvertTo-Json -Depth 8 | Set-Content "$output/plan.json" -Encoding UTF8
    Run-Step 'plan-run' @("$build/AIpet.Host.dll",'--run-plan',"$output/plan.json","$output/plan-result") 15
    $result=Get-Content "$output/plan-result/run-result.json" -Raw | ConvertFrom-Json
    if($result.motion.Status -ne 'completed' -or -not $result.coverage.RemovalConfirmed -or $result.error) { throw 'Plan result assertions failed' }
} catch { $failure=$_.ToString(); Write-Output $failure }
finally {
    $unchanged=@($sources | Where-Object { (Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256 }).Count -eq 0
    $binaries=@(Get-ChildItem data/build/host-integration -File -Filter *.dll | ForEach-Object { @{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    @{schema='aipet.host-verification/0.1';runId=$runId;nonce=[guid]::NewGuid().ToString('N');checkpoint=$checkpoint;
        upstream=$upstream;sourceFiles=$sources;sourceUnchanged=$unchanged;binaries=$binaries;steps=$steps.ToArray();error=$failure;
        execution='host-authorized';screenPixelsPersisted=$false;physicalDisplaysExercised=1;os=[Environment]::OSVersion.VersionString;
        planInput=$(if(Test-Path "$output/plan.json") { @{bytes=(Get-Item "$output/plan.json").Length;sha256=(Get-FileHash "$output/plan.json").Hash} });
        completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 10 | Set-Content "$output/receipt.json" -Encoding UTF8
    Write-Output "Receipt: $output/receipt.json"
}
if($failure -or -not $unchanged) { exit 1 }
exit 0
