$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:NUMBA_CACHE_DIR = Join-Path $projectRoot 'artifacts/numba-cache'
$testTemp = Join-Path $projectRoot 'artifacts/test-temp'
New-Item -ItemType Directory -Force $testTemp | Out-Null
$env:TEMP = $testTemp
$env:TMP = $testTemp
Push-Location $projectRoot
try {
    python -m unittest discover -s tests -v
    if ($LASTEXITCODE -ne 0) { throw 'Audio tests failed' }
    if (-not (Test-Path 'artifacts/smoke/Synthetic test.stemforge.json')) {
        python scripts/smoke_pipeline.py
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture failed' }
    }
    dotnet run --project tests/BridgeSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge tests failed' }
    dotnet run --project tests/StoreSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'SQLite tests failed' }
    python scripts/smoke_analysis.py
    if ($LASTEXITCODE -ne 0) { throw 'Tempo/key fixture failed' }
    dotnet run --project tests/UiSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'WPF layout / interaction tests failed' }
    dotnet run --project tests/GuideSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Embedded guide tests failed' }
    dotnet run --project tests/LocalizationSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Localization tests failed' }
    dotnet run --project tests/SunoLyricsSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Direct Suno lyrics / menus / export folder tests failed' }
    dotnet run --project tests/SunoTimingSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Authenticated Suno timing browser tests failed' }
    dotnet run --project tests/LyricsPlaybackSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Lyrics playback / selection / clear tests failed' }
    dotnet run --project tests/TransportSmoke -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Continuous playback / collapsible steps tests failed' }
    if (Test-Path 'artifacts/cascade-cpu-v0142/CPU test.stemforge.json') {
        dotnet run --project tests/LivePreviewSmoke -c Release -- $projectRoot
        if ($LASTEXITCODE -ne 0) { throw 'Live RAM playback / DSP update / export tests failed' }
    } else { Write-Host 'LivePreviewSmoke requires the real CPU cascade fixture; skipped.' }
} finally { Pop-Location }
