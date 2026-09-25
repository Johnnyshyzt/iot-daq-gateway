@echo off
setlocal EnableExtensions
REM IoT DAQ Gateway - install as a Windows service (auto-start after reboot).
REM Run this file as Administrator. Encoding: UTF-8.

chcp 65001 >nul
cd /d "%~dp0"

set "SERVICE_NAME=IotDaqGateway"
set "DISPLAY_NAME=IoT DAQ Gateway"
set "EXE=%~dp0Host.exe"

echo ========================================
echo  安装 IoT 采集网关 Windows 服务
echo ========================================
echo 目录: %~dp0
echo.

net session >nul 2>&1
if errorlevel 1 (
  echo [错误] 请右键本脚本 →「以管理员身份运行」。
  exit /b 1
)

if not exist "%EXE%" (
  echo [错误] 未找到 Host.exe。请先解压完整的 win-x64 发布包。
  exit /b 1
)

if not exist "%~dp0Fwlib64.dll" (
  echo [警告] 本目录没有 Fwlib64.dll。
  echo         fanuc.focas 设备会保持 offline；请把授权的 64 位库放到：
  echo         %~dp0Fwlib64.dll
  echo         不要把该 DLL 提交进 git 或打进 Docker 镜像。
  echo.
)

sc query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
  echo 服务已存在，正在停止并删除旧服务...
  sc stop "%SERVICE_NAME%" >nul 2>&1
  timeout /t 3 /nobreak >nul
  sc delete "%SERVICE_NAME%" >nul
  timeout /t 2 /nobreak >nul
)

sc create "%SERVICE_NAME%" binPath= "\"%EXE%\"" start= auto DisplayName= "%DISPLAY_NAME%"
if errorlevel 1 (
  echo [错误] sc create 失败。
  exit /b 1
)

sc description "%SERVICE_NAME%" "内网 CNC 采集网关。浏览器打开 http://127.0.0.1:5080 发布配置，同一进程会重新加载 data\published。现场不需要 .NET SDK。"
sc failure "%SERVICE_NAME%" reset= 86400 actions= restart/5000/restart/10000/restart/30000 >nul

if not exist "%~dp0service.env" (
  echo [警告] 没有 service.env。MQTT 密码不会注入。
  echo         复制 service.env.example 为 service.env，填写 MQTT_USER 和 MQTT_PASSWORD。
  echo.
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0service-env.ps1" -Mode service -ServiceName "%SERVICE_NAME%"
if errorlevel 1 (
  echo [错误] 写入服务环境变量失败。MQTT_USER / MQTT_PASSWORD 未注入。
  exit /b 1
)

sc start "%SERVICE_NAME%"
if errorlevel 1 (
  echo [错误] 服务已创建但启动失败。请查看本目录 logs\ 下当天的 gateway-yyyyMMdd.log
  exit /b 1
)

echo.
echo [完成] 服务 %SERVICE_NAME% 已启动，开机将自动运行。
echo 页面:   http://127.0.0.1:5080
echo 登录:   现场模式不使用 admin/admin。一次性密码在 data\auth\bootstrap-password.txt
echo         登录后必须修改密码。角色仍是本机 admin / engineer / viewer。
echo MQTT:   密码只在 service.env。修改后重新以管理员运行本脚本。
echo 改配置: 在页面发布。采集读取 data\published，不必为此重启服务。
echo 日志:   %~dp0logs\
echo 状态:   sc query %SERVICE_NAME%
echo 卸载:   以管理员运行 uninstall-service.bat
endlocal
exit /b 0
