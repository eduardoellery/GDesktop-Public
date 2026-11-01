$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& ./.dotnet/dotnet.exe build src/GDesktop/GDesktop.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
& node tests/GDesktop.Smoke/smoke.mjs
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
$env:OFFICE_APP = 'src/GDesktop/bin/Release/net10.0-windows10.0.19041.0/GDesktop.exe'
& node tests/GDesktop.Smoke/office.mjs
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de Office.' }
