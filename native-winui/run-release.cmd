@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Run-Release.ps1
exit /b %errorlevel%
