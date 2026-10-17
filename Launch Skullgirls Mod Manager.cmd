@echo off
setlocal
cd /d "%~dp0"
if not exist "dist\SkullgirlsModManager.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1"
  if errorlevel 1 (
    echo Build failed. See the output above.
    pause
    exit /b 1
  )
)
start "" "%~dp0dist\SkullgirlsModManager.exe"
