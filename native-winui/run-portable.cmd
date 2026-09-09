@echo off
setlocal
cd /d "%~dp0"
if not exist "data" mkdir "data"
set "VITANCUT_DATABASE_PATH=%~dp0data\database.json"
start "" "%~dp0VitanCut.WinUI.exe"
