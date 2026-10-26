@echo off
if exist "%~dp0artifacts\SunoAudioForge\SunoAudioForge.exe" (
  start "Suno Audio Forge" "%~dp0artifacts\SunoAudioForge\SunoAudioForge.exe"
  exit /b 0
)
echo Build the app first: powershell -File scripts\build.ps1 -Publish
echo Or extract apps\SunoAudioForge-v0.15.0.zip and run SunoAudioForge.exe.
pause
