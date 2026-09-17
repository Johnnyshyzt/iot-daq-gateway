@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
REM Foreground run for first-time checks. Close this window to stop. Use install-service.bat for autostart.

if not exist "%~dp0Gateway.Host.exe" (
  echo [错误] 未找到 Gateway.Host.exe
  exit /b 1
)

if not exist "%~dp0gateway.yaml" (
  echo [错误] 未找到 gateway.yaml
  exit /b 1
)

echo 前台运行网关。日志同时写入 logs\ 。按 Ctrl+C 结束。
echo.
"%~dp0Gateway.Host.exe" --config "%~dp0gateway.yaml"
echo.
echo 进程已退出，退出码 %ERRORLEVEL%。
pause
