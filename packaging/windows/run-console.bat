@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
REM Foreground run. Loads service.env, then starts Host. Use install-service.bat for autostart.

if not exist "%~dp0Host.exe" (
  echo [错误] 未找到 Host.exe
  exit /b 1
)

where powershell >nul 2>&1
if errorlevel 1 (
  echo [错误] 未找到 PowerShell，无法加载 service.env。
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0service-env.ps1" -Mode run
set "RC=%ERRORLEVEL%"
echo.
echo 进程已退出，退出码 %RC%。
echo 若是首次启动，一次性密码在 data\auth\bootstrap-password.txt。不要使用 admin/admin。
pause
exit /b %RC%
