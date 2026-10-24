$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $root
$runId = [guid]::NewGuid().ToString('N')
$out = Join-Path $root "data\receipts\pets\$runId"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $root 'data\toolchains\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root 'data\cache\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root 'data\cache\nuget-http'
$env:TEMP = Join-Path $root 'data\temp'
$env:TMP = $env:TEMP
$steps = [Collections.Generic.List[object]]::new()
$sources = @(Get-ChildItem tests/pet-hosts,tools/pet-hosts -File -Recurse | ForEach-Object {
    @{ path=$_.FullName; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
$upstreams = @{
    vpet='0c2e795b8e81485e448675316ba088d5226d908d'
    esheep='ac4e5dab57f6e4aa9c78ee0cfba4ffff8a680ad8'
}
foreach ($name in $upstreams.Keys) {
    $actual = & git -c "safe.directory=$($root.Replace('\','/'))/data/upstream/$name" -C "data/upstream/$name" rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual -ne $upstreams[$name]) { throw "Upstream checkpoint mismatch: $name" }
    $dirty = & git -c "safe.directory=$($root.Replace('\','/'))/data/upstream/$name" -C "data/upstream/$name" diff --name-only
    if ($dirty) { throw "Tracked upstream source changed: $name" }
}
function Run-Step([string]$name,[string]$exe,[string[]]$arguments,[int]$timeout=30) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/$name.stdout.log" -RedirectStandardError "$out/$name.stderr.log"
    $timedOut = -not $p.WaitForExit($timeout*1000)
    if ($timedOut) { $p.Kill(); $p.WaitForExit() }
    $p.Refresh()
    $steps.Add(@{ name=$name; exitCode=$p.ExitCode; timedOut=$timedOut; processExited=$p.HasExited;
        executable=$exe; executableSha256=(Get-FileHash -LiteralPath $exe).Hash })
    Write-Output "$name exit=$($p.ExitCode) timeout=$timedOut"
    return ($p.ExitCode -eq 0 -and -not $timedOut)
}
$failure = $null
try {
    $dotnet = Join-Path $root 'data\toolchains\dotnet\dotnet.exe'
    $vpetBuild = Join-Path $root 'data\build\vpet-probe'
    $result = Run-Step 'vpet-build' $dotnet @('build','tests/pet-hosts/vpet-probe.csproj','-c','Release','-o',$vpetBuild) 60
    if ($result[-1] -ne $true) { throw 'VPet build failed' }
    $result = Run-Step 'vpet-runtime' $dotnet @("$vpetBuild/vpet-probe.dll","$out/fixtures")
    if ($result[-1] -ne $true) { throw 'VPet runtime failed' }

    # The Portable source has no stdole usage. Omit only the unused COM build reference;
    # leave every tracked upstream file unchanged.
    $project = Join-Path $root 'data\upstream\esheep\src\DesktopPet_AIPetProbe.csproj'
    [xml]$xml = Get-Content data/upstream/esheep/src/DesktopPet_Portable.csproj -Raw
    $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('m','http://schemas.microsoft.com/developer/msbuild/2003')
    @($xml.SelectNodes('//m:COMReference',$ns)) | ForEach-Object { $_.ParentNode.RemoveChild($_) | Out-Null }
    $xml.Save($project)
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $msbuild = & $vswhere -latest -products '*' -find MSBuild/**/Bin/MSBuild.exe | Select-Object -First 1
    $csc = Join-Path (Split-Path $msbuild -Parent) 'Roslyn\csc.exe'
    $runtime = Join-Path $root 'data\build\esheep-probe'
    $framework = Join-Path $root 'data\toolchains\net472\build\.NETFramework\v4.7.2'
    $result = Run-Step 'esheep-build' $msbuild @($project,'/nologo','/v:minimal','/p:Configuration=Release','/p:Platform=x64',"/p:FrameworkPathOverride=$framework","/p:OutputPath=$runtime/","/p:IntermediateOutputPath=$runtime/obj/",'/p:GenerateManifests=false','/p:DocumentationFile=','/p:CodeAnalysisRuleSet=') 60
    if ($result[-1] -ne $true) { throw 'eSheep build failed' }
    Copy-Item data/upstream/esheep/src/Resources/animations.xml "$runtime/motion-fixture.xml"
    foreach ($test in 'ESheepResourceProbe','TransitionRuntimeTests','MotionAuditTests','RuntimeMotionTests') {
        $source = if ($test -eq 'ESheepResourceProbe') { 'tests/pet-hosts/ESheepResourceProbe.cs' } else { "data/upstream/esheep/tests/$test.cs" }
        $result = Run-Step "$test-build" $csc @('/nologo','/platform:x64','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xml.dll',"/r:$runtime/DesktopPet.exe","/out:$runtime/$test.exe",$source)
        if ($result[-1] -ne $true) { throw "$test compile failed" }
        $arguments = if ($test -eq 'ESheepResourceProbe') { @("$root/data/upstream/esheep/src/Resources/animations.xml") } else { @('1','0') }
        # Upstream tests set their own working directory before accessing fixtures.
        $null = Run-Step "$test-runtime" "$runtime/$test.exe" $arguments
    }
} catch { $failure = $_.ToString(); Write-Output $failure }
finally {
    $changed = @($sources | Where-Object { (Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256 })
    $checkpoint = & git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
    @{ schema='aipet.pet-probe/0.1'; runId=$runId; nonce=[guid]::NewGuid().ToString('N');
        execution='host-authorized'; checkpoint=$checkpoint; sourceFiles=$sources; sourceUnchanged=($changed.Count -eq 0);
        upstreams=$upstreams; steps=@($steps.ToArray()); error=$failure; screenPixelsPersisted=$false;
        boundary='single physical screen; synthetic resources; upstream failures preserved';
        completedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 8 | Set-Content "$out/receipt.json" -Encoding UTF8
    Write-Output "Receipt: $out/receipt.json"
}
if ($failure -or @($steps | Where-Object { $_.exitCode -ne 0 -or $_.timedOut }).Count -gt 0) { exit 1 }
exit 0
