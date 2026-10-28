@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Usage: setup-engine.bat [python.exe] [cpu|cu130] [/nopause]
rem Resolve files relative to this script, regardless of the current folder.
for %%I in ("%~dp0..") do set "engineRoot=%%~fI"
set "basePython=python"
if not "%~1"=="" set "basePython=%~1"
set "engineDevice=cpu"
if not "%~2"=="" set "engineDevice=%~2"
set "enginePython=%engineRoot%\.venv\Scripts\python.exe"
set "setupResult=1"

if /I "%engineDevice%"=="cpu" goto device_ok
if /I "%engineDevice%"=="cu130" goto device_ok
echo ERROR: Device must be cpu or cu130.
goto finish

:device_ok
echo Suno Audio Forge - audio engine setup
echo Python: "%basePython%"
echo Device: %engineDevice%
echo Environment: "%engineRoot%\.venv"
echo Internet is required. PyTorch downloads can be large.
echo.
if not exist "%engineRoot%\engine\requirements.txt" (
    echo ERROR: engine\requirements.txt is missing. Extract the complete app first.
    goto finish
)
"%basePython%" -c "import sys, struct; sys.exit(0 if sys.version_info[:2] in ((3,12),(3,13)) and struct.calcsize('P') == 8 else 1)"
if errorlevel 1 (
    echo ERROR: Python 3.12 or 3.13, 64-bit is required.
    echo Install Python or pass the full path to python.exe as the first argument.
    goto finish
)

echo [1/4] Creating the isolated Python environment...
"%basePython%" -m venv "%engineRoot%\.venv"
if errorlevel 1 (
    echo ERROR: Could not create .venv. Check folder permissions and available disk space.
    goto finish
)
echo [2/4] Updating pip...
"%enginePython%" -m pip install --upgrade pip
if errorlevel 1 goto pip_failed
echo [3/4] Installing PyTorch and torchaudio...
"%enginePython%" -m pip install torch==2.9.1 torchaudio==2.9.1 --index-url "https://download.pytorch.org/whl/%engineDevice%"
if errorlevel 1 goto pip_failed
echo [4/4] Installing the audio engine libraries...
"%enginePython%" -m pip install -r "%engineRoot%\engine\requirements.txt"
if errorlevel 1 goto pip_failed

echo.
echo SUCCESS. In Settings, set Python executable to:
echo %enginePython%
echo Then click Save and check engine.
echo FFmpeg must be in PATH or selected in Settings.
echo For RoFormer / DrumSep, use the app's Install engine RoFormer / DrumSep button.
set "setupResult=0"
goto finish

:pip_failed
echo.
echo ERROR: Library installation failed. See the pip error above.
echo Fix the reported issue, then run this file again.

:finish
echo.
if /I not "%~3"=="/nopause" pause
exit /b %setupResult%
