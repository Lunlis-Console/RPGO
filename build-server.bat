@echo off
cd /d "%~dp0"
echo ============================================
echo  Server build script
echo ============================================
echo.
echo   [1] Windows (win-x64)
echo   [2] Linux   (linux-x64)
echo.
set /p choice="Choose target (1/2): "

if "%choice%"=="1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-server.ps1" -Runtime win-x64
)
if "%choice%"=="2" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-server.ps1" -Runtime linux-x64
)

echo.
pause
