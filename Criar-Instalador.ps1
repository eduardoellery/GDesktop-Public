param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $IsccPath) {
    $taskCandidates = @(
        "$PSScriptRoot/tools/inno-setup/ISCC.exe",
        "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
        "$env:ProgramFiles/Inno Setup 7/ISCC.exe",
        "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe",
        "$env:LOCALAPPDATA/Programs/Inno Setup 7/ISCC.exe"
    )
    $IsccPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Instale Inno Setup 6.7.4 ou superior, ou informe -IsccPath com o caminho de ISCC.exe. https://jrsoftware.org/isdl.php'
}
$taskDotnet = Join-Path $PSScriptRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'Execute Preparar.ps1 primeiro.' }
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskPublish = Join-Path $PSScriptRoot 'artifacts/installer-app'
& $taskDotnet publish src/GDesktop/GDesktop.csproj -c Release -r win-x64 --self-contained true -o $taskPublish
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar a aplicação.' }
[xml]$taskProject = Get-Content -LiteralPath 'src/GDesktop/GDesktop.csproj'
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
& $IsccPath "/DAppVersion=$taskVersion" "/DPublishDir=$taskPublish" 'installer/GDesktop.iss'
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
$taskSetup = Join-Path $PSScriptRoot "artifacts/installer/GDesktop-Setup-$taskVersion-win-x64.exe"
$taskHash = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$taskSetup.sha256", "$taskHash  $([IO.Path]::GetFileName($taskSetup))`n")
Write-Host "Instalador pronto: $taskSetup"
