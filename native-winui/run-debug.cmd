@echo off
setlocal
cd /d "%~dp0.."
if not exist "native-winui\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\VitanCut.WinUI.exe" (
  call native-winui\build-release.cmd || exit /b 1
)
"native-winui\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\VitanCut.WinUI.exe"
echo.
echo VitanCut.WinUI exited with code %errorlevel%.
pause
