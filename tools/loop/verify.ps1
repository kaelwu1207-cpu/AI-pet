$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $root
$id=[guid]::NewGuid().ToString('N')
$out=Join-Path $root "data\receipts\loop\$id"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $root 'data\toolchains\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root 'data\cache\nuget'
$env:NUGET_HTTP_CACHE_PATH=Join-Path $root 'data\cache\nuget-http'
$env:TEMP=Join-Path $root 'data\temp';$env:TMP=$env:TEMP
$dotnet=Join-Path $root 'data\toolchains\dotnet\dotnet.exe'
$shell=Join-Path $PSHOME 'pwsh.exe'
$checkpoint=& git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
$upstream=& git -c "safe.directory=$($root.Replace('\','/'))/data/upstream/vpet" -C data/upstream/vpet rev-parse HEAD
if($LASTEXITCODE -ne 0 -or $upstream -ne '0c2e795b8e81485e448675316ba088d5226d908d'){throw 'Upstream mismatch'}
$sources=@(Get-ChildItem src/host,src/capture-rust,tests/pet-hosts,tools/loop,tools/capture,tools/host -Recurse -File |
    Where-Object {$_.Extension -in '.cs','.csproj','.props','.rs','.hlsl','.toml','.lock','.py','.ps1'} |
    ForEach-Object { @{path=$_.FullName;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$steps=[Collections.Generic.List[object]]::new()
function Step([string]$name,[string]$exe,[string[]]$arguments,[int]$seconds) {
    $start=[DateTime]::UtcNow
    $p=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/$name.stdout.log" -RedirectStandardError "$out/$name.stderr.log"
    $timeout=-not $p.WaitForExit($seconds*1000)
    if($timeout){$p.Kill();$p.WaitForExit()};$p.Refresh()
    $steps.Add(@{name=$name;exitCode=$p.ExitCode;timedOut=$timeout;processExited=$p.HasExited;processId=$p.Id;
        startedUtc=$start.ToString('o');completedUtc=[DateTime]::UtcNow.ToString('o')})
    Write-Output "$name exit=$($p.ExitCode) timeout=$timeout"
    if($timeout -or $p.ExitCode -ne 0){throw "$name failed"}
}
$failure=$null
try {
    Step 'rust-build-clippy' $shell @('-NoProfile','-File',"$root/tools/capture/build-rust.ps1") 60
    Step 'logic-build' $dotnet @('build','tests/pet-hosts/loop-logic.csproj','-c','Release','-o',"$root/data/build/loop-logic",'-warnaserror','-m:1') 30
    Step 'logic' $dotnet @("$root/data/build/loop-logic/loop-logic.dll") 5
    Step 'gpu-loop-build' $dotnet @('build','tests/pet-hosts/loop-gpu.csproj','-c','Release','-o',"$root/data/build/loop-gpu",'-warnaserror','-m:1') 30
    Step 'gpu-loop' $dotnet @("$root/data/build/loop-gpu/loop-gpu.dll","$out/fixtures") 35
    Step 'host-regression' $shell @('-NoProfile','-File',"$root/tools/host/verify.ps1") 60
} catch {$failure=$_.ToString();Write-Output $failure}
finally {
    $same=@($sources|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256}).Count -eq 0
    $binaries=@("$root/data/build/capture-rust/debug/observe.exe","$root/data/build/loop-gpu/AIpet.Host.dll")|ForEach-Object{
        if(Test-Path -LiteralPath $_){@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}}
    }
    @{schema='aipet.loop-verification/0.1';runId=$id;nonce=[guid]::NewGuid().ToString('N');checkpoint=$checkpoint;upstream=$upstream;
        sourceFiles=$sources;sourceUnchanged=$same;binaries=@($binaries);steps=$steps.ToArray();error=$failure;execution='host-authorized';
        physicalDisplaysExercised=1;pixelsPersisted=$false;gpuStatisticsPixelReadback=$false;hostRegressionUsesCpuFixtureProbe=$true;statisticsRoi='320x128 owned fixture';
        completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 10|Set-Content "$out/receipt.json" -Encoding UTF8
    Write-Output "Receipt: $out/receipt.json"
}
if($failure -or -not $same){exit 1};exit 0
