@echo off
setlocal
cd /d "%~dp0.."
set DOTNET_CLI_HOME=%CD%\.dotnet-home
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
dotnet build native-winui\VitanCut.WinUI.csproj -c Release -p:Platform=x64 -p:WindowsTargetPlatformVersion=10.0.26100.0
