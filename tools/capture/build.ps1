param([ValidateSet('tests','probe')][string]$Target = 'tests')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$out = Join-Path $root 'data\build\capture'
$temp = Join-Path $root 'data\temp\capture'
$logs = Join-Path $root 'data\logs\capture'
New-Item -ItemType Directory -Path $out,$temp,$logs -Force | Out-Null
$env:TEMP = $temp
$env:TMP = $temp
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC x64 build tools not found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$sources = if ($Target -eq 'tests') { @('tests\capture\tracker_tests.cpp') } else { @('src\capture\wgc_capture.cpp', 'src\capture\capture_probe.cpp') }
$sourceArgs = ($sources | ForEach-Object { '"' + (Join-Path $root $_) + '"' }) -join ' '
$exe = Join-Path $out "$Target.exe"
$command = 'call "' + $vcvars + '" >nul && cl /nologo /std:c++20 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /I"' + $root + '\src\capture" ' + $sourceArgs + ' /Fe:"' + $exe + '" /link windowsapp.lib d3d11.lib dxgi.lib user32.lib shcore.lib gdi32.lib'
Push-Location $out
try {
    & $env:ComSpec /d /s /c $command 2>&1 | Tee-Object -FilePath (Join-Path $logs ("build-$Target-" + [guid]::NewGuid().ToString('N') + '.log'))
    $code = $LASTEXITCODE
} finally { Pop-Location }
if ($code -ne 0) { exit $code }
Write-Output $exe
