@echo off
setlocal EnableExtensions
REM IoT DAQ Gateway - uninstall Windows service. Does not delete YAML, logs, or Fwlib64.dll.

chcp 65001 >nul
cd /d "%~dp0"

set "SERVICE_NAME=IotDaqGateway"

echo ========================================
echo  卸载 IoT 采集网关 Windows 服务
echo ========================================
echo.

net session >nul 2>&1
if errorlevel 1 (
  echo [错误] 请右键本脚本 →「以管理员身份运行」。
  exit /b 1
)

sc query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
  echo 服务 %SERVICE_NAME% 不存在，无需卸载。
  exit /b 0
)

echo 正在停止服务...
sc stop "%SERVICE_NAME%" >nul 2>&1
timeout /t 3 /nobreak >nul
sc delete "%SERVICE_NAME%"
if errorlevel 1 (
  echo [错误] sc delete 失败。
  exit /b 1
)

echo.
echo [完成] 服务已删除。开机将不再自动启动。
echo gateway.yaml、logs\、Fwlib64.dll 仍保留在本目录，可手动删除。
endlocal
exit /b 0
