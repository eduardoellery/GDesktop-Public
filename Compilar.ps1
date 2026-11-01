$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& "$env:DOTNET_ROOT/dotnet.exe" publish src/GDesktop/GDesktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/GDesktop
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
Write-Host 'Pronto: artifacts/GDesktop/GDesktop.exe'
