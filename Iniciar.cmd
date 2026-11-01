@echo off
cd /d "%~dp0"
if not exist "artifacts\GDesktop\GDesktop.exe" (
  echo Execute Compilar.ps1 antes de iniciar.
  pause
  exit /b 1
)
start "" "artifacts\GDesktop\GDesktop.exe"
