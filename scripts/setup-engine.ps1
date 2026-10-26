param([string]$Python = 'python', [ValidateSet('cpu','cu130')][string]$Device = 'cpu')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $projectRoot '.venv'
& $Python -m venv $venv
if ($LASTEXITCODE -ne 0) { throw 'Could not create Python environment' }
$enginePython = Join-Path $venv 'Scripts/python.exe'
& $enginePython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'pip bootstrap failed' }
& $enginePython -m pip install torch==2.9.1 torchaudio==2.9.1 --index-url "https://download.pytorch.org/whl/$Device"
if ($LASTEXITCODE -ne 0) { throw 'PyTorch installation failed' }
& $enginePython -m pip install -r (Join-Path $projectRoot 'engine/requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Engine installation failed' }
Write-Host "In Suno Audio Forge settings, set Python executable to: $enginePython"
Write-Host 'FFmpeg must be in PATH or selected in the app settings.'
