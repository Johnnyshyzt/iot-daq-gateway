@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
REM Foreground run for first-time checks. Close this window to stop. Use install-service.bat for autostart.

if not exist "%~dp0Host.exe" (
  echo [错误] 未找到 Host.exe
  exit /b 1
)

echo 前台运行 Host。页面 http://127.0.0.1:5080  日志写入 logs\ 。按 Ctrl+C 结束。
echo.
"%~dp0Host.exe"
echo.
echo 进程已退出，退出码 %ERRORLEVEL%。
pause
