@echo off
if not exist "%~dp0app\StructureEditor.exe" (
  echo Please build the application first: powershell -File scripts\build.ps1
  pause
  exit /b 1
)
start "" "%~dp0app\StructureEditor.exe"
