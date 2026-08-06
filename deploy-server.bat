@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-server.ps1" -ServerIp 195.19.144.151
pause
