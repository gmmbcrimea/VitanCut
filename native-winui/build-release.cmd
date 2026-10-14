@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Release.ps1
exit /b %errorlevel%
