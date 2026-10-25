$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));Set-Location -LiteralPath $root
$output="$root/data/receipts/resident/verify-$([guid]::NewGuid().ToString('N'))";New-Item -ItemType Directory -Path $output|Out-Null
$env:DOTNET_CLI_HOME="$root/data/toolchains/dotnet-home";$env:NUGET_PACKAGES="$root/data/cache/nuget";$env:NUGET_HTTP_CACHE_PATH="$root/data/cache/nuget-http"
$env:TEMP="$root/data/temp";$env:TMP=$env:TEMP
$dotnet="$root/data/toolchains/dotnet/dotnet.exe"
$checkpoint=& git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
$sources=@(Get-ChildItem src/host,src/capture-rust,assets,contracts,tests/pet-hosts,tools/resident -Recurse -File|Where-Object{$_.Extension -in '.cs','.csproj','.rs','.hlsl','.toml','.lock','.json','.png','.ps1'}|ForEach-Object{@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$steps=[Collections.Generic.List[object]]::new();$failure=$null
function Step($name,[string[]]$arguments,$seconds,$exe=$dotnet){
    $p=Start-Process $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$output/$name.stdout.log" -RedirectStandardError "$output/$name.stderr.log"
    $timeout=-not $p.WaitForExit($seconds*1000);if($timeout){$p.Kill($true);$p.WaitForExit()};$p.Refresh()
    $steps.Add(@{name=$name;processId=$p.Id;exitCode=$p.ExitCode;timedOut=$timeout});Write-Output "$name exit=$($p.ExitCode) timeout=$timeout"
    if($timeout -or $p.ExitCode -ne 0){throw "$name failed"}
}
try{
    Step 'build-resident-tests' @('build','tests/pet-hosts/resident-tests.csproj','--no-restore','-c','Release','-o','data/build/resident-tests','-warnaserror','-m:1','-nr:false','-p:UseSharedCompilation=false') 40
    Step 'lifecycle-matrix' @('data/build/resident-tests/resident-tests.dll',"$root/assets","$root/data/build/capture-rust/release/observe.exe","$output/lifecycle") 30
    Step 'capture-lease' @('data/build/resident-tests/resident-tests.dll','--lease',"$root/data/build/capture-rust/release/observe.exe") 10
    Step 'build-assets-tests' @('build','tests/pet-hosts/asset-tests.csproj','--no-restore','-c','Release','-o','data/build/asset-tests','-warnaserror','-m:1','-nr:false','-p:UseSharedCompilation=false') 30
    Step 'assets' @('data/build/asset-tests/asset-tests.dll',"$root/assets") 5
    Step 'build-host' @('build','src/host/AIpet.Host.csproj','--no-restore','-c','Release','-o','data/build/resident','-warnaserror','-m:1','-nr:false','-p:UseSharedCompilation=false') 30
    Step 'tray-smoke' @('-NoProfile','-File',"$root/tools/resident/run.ps1",'-Seconds','20','-ExercisePause') 40 (Join-Path $PSHOME 'pwsh.exe')
}catch{$failure=$_.ToString()}
finally{
    $same=@($sources|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256}).Count -eq 0
    @{schema='aipet.resident-verification/1';runId=Split-Path $output -Leaf;nonce=[guid]::NewGuid().ToString('N');checkpoint=$checkpoint;
        sourceFiles=$sources;sourceUnchanged=$same;steps=$steps.ToArray();error=$failure;pixelsPersisted=$false;physicalDisplaysExercised=1;
        completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 8|Set-Content "$output/receipt.json" -Encoding utf8
    Write-Output "RECEIPT=$output/receipt.json ERROR=$failure"
}
if($failure -or -not $same){exit 1};exit 0
