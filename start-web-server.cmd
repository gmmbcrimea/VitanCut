@echo off
cd /d "%~dp0"
echo [%date% %time%] starting in %cd%> server-launch.log
"C:\Program Files\nodejs\node.exe" server.js >> server-launch.log 2>&1
echo [%date% %time%] exited with %errorlevel%>> server-launch.log
