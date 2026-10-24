param(
    [ValidateSet('library','native','rust')][string]$Backend = 'library',
    [ValidateRange(1,16)][int]$MinimumDisplays = 2,
    [ValidateRange(2,15)][int]$Seconds = 4
)
$ErrorActionPreference = 'Stop'
if ($Backend -eq 'rust' -and $Seconds -ne 4) { throw 'Rust probe currently uses a fixed four-second window' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runId = [guid]::NewGuid().ToString('N')
$nonce = [guid]::NewGuid().ToString('N')
$receiptDir = Join-Path $root "data\receipts\capture\$runId"
New-Item -ItemType Directory -Path $receiptDir -Force | Out-Null
$inputs = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\capture'),(Join-Path $root 'src\capture-rust'),(Join-Path $root 'tools\capture') -File -Recurse |
    Where-Object { $_.Extension -in '.py','.cpp','.hpp','.ps1','.rs','.toml','.lock' } |
    Sort-Object FullName | ForEach-Object { @{path=$_.FullName; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$checkpoint = & git -c "safe.directory=$($root.Replace('\','/'))" -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot bind verification to Git checkpoint' }
if ($Backend -eq 'library') {
    $exe = Join-Path $root 'data\envs\capture\Scripts\python.exe'
    $arguments = @('-B', (Join-Path $root 'tools\capture\probe_library.py'), '--seconds', "$Seconds", '--min-displays', "$MinimumDisplays")
} elseif ($Backend -eq 'rust') {
    $exe = Join-Path $root 'data\build\capture-rust\debug\aipet-gpu-probe.exe'
    $arguments = @("$MinimumDisplays")
} else {
    $exe = Join-Path $root 'data\build\capture\probe.exe'
    $arguments = @('--seconds', "$Seconds", '--min-displays', "$MinimumDisplays", '--stimulus')
}
$stdout = Join-Path $receiptDir 'stdout.json'
$stderr = Join-Path $receiptDir 'stderr.log'
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$timedOut = -not $process.WaitForExit(($Seconds + 25) * 1000)
if ($timedOut) { $process.Kill(); $process.WaitForExit() }
$process.Refresh()
$exitCode = $process.ExitCode
$text = [IO.File]::ReadAllText($stdout)
$result = $null
try { $result = $text | ConvertFrom-Json } catch { }
$inputsUnchanged = $true
foreach ($inputFile in $inputs) {
    if ((Get-FileHash -LiteralPath $inputFile.path).Hash -ne $inputFile.sha256) { $inputsUnchanged = $false }
}
$record = [ordered]@{
    schemaVersion='aipet.verification/0.1'; project='AI-pet'; runId=$runId; nonce=$nonce
    execution='host-authorized'; userAuthorization='2026-10-25 project Hyper-V exemption'
    sourceCheckpoint=$checkpoint; sourceFiles=$inputs; sourceUnchanged=$inputsUnchanged
    executable=$exe; executableSha256=(Get-FileHash -LiteralPath $exe).Hash
    os=[Environment]::OSVersion.VersionString; processId=$process.Id
    startedUtc=$started.ToString('o'); completedUtc=[DateTime]::UtcNow.ToString('o')
    timeoutSeconds=$Seconds+25; timedOut=$timedOut; exitCode=$exitCode
    minimumDisplays=$MinimumDisplays; backend=$Backend; stdout='stdout.json'; stderr='stderr.log'
    result=$result; processExited=$process.HasExited; screenPixelsPersisted=$false
}
$record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $receiptDir 'receipt.json') -Encoding UTF8
Write-Output $text
Write-Output "Receipt: $receiptDir\receipt.json"
if ($timedOut -or -not $inputsUnchanged -or $null -eq $result) { exit 1 }
exit $exitCode
