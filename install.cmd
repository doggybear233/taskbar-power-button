@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo Installing Taskbar Power Button...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" echo Installation failed with exit code %RC%.
echo Press any key to close this window.
pause >nul
exit /b %RC%