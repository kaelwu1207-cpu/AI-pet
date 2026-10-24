$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$env:RUSTUP_HOME = Join-Path $root 'data\toolchains\rustup'
$env:CARGO_HOME = Join-Path $root 'data\cache\cargo'
$env:CARGO_TARGET_DIR = Join-Path $root 'data\build\capture-rust'
$env:TEMP = Join-Path $root 'data\temp'
$env:TMP = $env:TEMP
$cargo = Join-Path $env:CARGO_HOME 'bin\cargo.exe'
$manifest = Join-Path $root 'src\capture-rust\Cargo.toml'
& $cargo build --locked --manifest-path $manifest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $cargo clippy --locked --manifest-path $manifest -- -D warnings
exit $LASTEXITCODE
