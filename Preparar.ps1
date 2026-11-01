$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$taskSdk = Join-Path $PSScriptRoot '.dotnet'
& "$PSScriptRoot/tools/dotnet-install.ps1" -Version '10.0.403' -InstallDir $taskSdk -NoPath
if (-not (Test-Path (Join-Path $taskSdk 'dotnet.exe'))) { throw 'SDK .NET ausente.' }
Write-Host 'Ambiente preparado. Execute Compilar.ps1 para gerar a aplicação.'
