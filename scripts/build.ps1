param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
Push-Location $projectRoot
try {
    if ($Publish) {
        dotnet publish src/StemForge/StemForge.csproj -c Release --self-contained false -o artifacts/SunoAudioForge
    } else {
        dotnet build StemForge.sln -c Release
    }
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
