# Windows 内网安装（生产路径）

第一期生产采集路径是 **Windows x64 采集机**：自包含 zip + 现场自备 `Fwlib64.dll` + 外置 `gateway.yaml` + **本机 Web 配置控制台** + Windows 服务开机自启。

**不要把 Linux Docker 当作生产 FOCAS 路径。** Compose 镜像只用于 Fake 演示 / 附属 Mosquitto。

现场 **不需要** 安装 .NET SDK（包括钉死的 10.0.203），也不需要 git 检出本仓库。构建机才需要 SDK。

## 发布形态：自包含 win-x64

选择 **self-contained（自包含）** 而不是 framework-dependent：

| | 自包含（本仓库采用） | 依赖框架 |
| --- | --- | --- |
| 现场要装什么 | 无（zip 内带 .NET 10 运行时） | 仍要装 .NET **运行时** 10，不是 SDK，但内网机经常也没有 |
| 包体积 | 更大（约几十 MB） | 更小 |
| 离线内网 | 解压即可 | 还要先拿到 runtime 安装包 |

不使用单文件（`PublishSingleFile=false`），以便把 `Fwlib64.dll` 放到与 `Gateway.Host.exe` 同一目录，供 P/Invoke 加载。

构建（开发机 / CI，需要 SDK **10.0.203**）：

```bash
# Linux 交叉编译或 Windows 本机均可
./scripts/pack-win-x64.sh
# Windows:
#   powershell -File scripts/pack-win-x64.ps1
```

产物：`artifacts/win-x64/iot-daq-gateway-<version>-win-x64.zip`。GitHub Actions 的 `pack-win-x64` 作业会上传同名 artifact。

## 现场安装

1. 把 zip 解压到固定目录，例如 `C:\iot-daq-gateway\`。必须保留全部文件，不要只拷 exe。
2. **把授权的 64 位 `Fwlib64.dll` 放到与 `Gateway.Host.exe` 同一目录。** 仓库和镜像从不附带该文件。缺库时进程仍运行，设备 `$status=offline`。
3. **建议先前台验证**：双击 `run-console.bat`。
4. **打开 Web 控制台（第一配置路径，不必手改 YAML）：**
   - 本机：浏览器打开 http://127.0.0.1:8080/
   - 内网其它电脑：http://采集机IP:8080/ （示例配置 `console.bind: 0.0.0.0`）
   - 默认口令见 `gateway.yaml` 的 `console.token`（发布包为 `change-me`，请立刻改掉；也可用环境变量 `GATEWAY_CONSOLE_TOKEN`）
   - 在页面里填写站点 / 网关 id、MQTT 地址与端口、扫描周期，并 **添加多台设备**（`fanuc.focas` 或演示用 `fanuc.fake`：id、机床 IP、端口、是否启用）
   - 点 **保存并应用**：写回同一份 `gateway.yaml`，采集循环热加载，**不必重新打包 zip、不必重装服务**
5. 确认后 **以管理员身份** 运行 `install-service.bat`：
   - 注册服务名 `IotDaqGateway`（显示名 IoT DAQ Gateway）
   - `start= auto`，开机自启
   - 失败后自动重启
6. 验收：订阅 `daq/#`，看到真实 `$status=online` 以及随机床变化的 `state`（不是 Fake 的 60 秒相位）。

无界面 / 脚本安装仍可直接编辑 `gateway.yaml`，它始终是权威配置。

手动等价命令（管理员 cmd）：

```bat
sc create IotDaqGateway binPath= "\"C:\iot-daq-gateway\Gateway.Host.exe\" --config \"C:\iot-daq-gateway\gateway.yaml\"" start= auto DisplayName= "IoT DAQ Gateway"
sc failure IotDaqGateway reset= 86400 actions= restart/5000/restart/10000/restart/30000
sc start IotDaqGateway
sc query IotDaqGateway
```

## 改配置（不必重建）

**优先用 Web 控制台** 保存并应用。YAML 会更新到安装目录下的 `gateway.yaml`。

若只改了文件、没用控制台：

```bat
sc stop IotDaqGateway
sc start IotDaqGateway
```

或「服务」管理器里重启 **IoT DAQ Gateway**。

配置查找顺序：`--config`（安装脚本已传入发布目录下的 `gateway.yaml`）、环境变量 `GATEWAY_CONFIG`、进程目录 `gateway.yaml`。服务的工作目录可能是 `C:\Windows\System32`，因此安装脚本始终传绝对路径。

控制台端口：`console.port`（默认 **8080**），或环境变量 `GATEWAY_CONSOLE_PORT`。绑定地址：`console.bind` 或 `GATEWAY_CONSOLE_BIND`。

**不要**在没有口令的情况下把控制台绑到 `0.0.0.0`：进程会拒绝监听管理口，采集仍继续。

## 卸载

以管理员运行 `uninstall-service.bat`，或：

```bat
sc stop IotDaqGateway
sc delete IotDaqGateway
```

不会删除 `gateway.yaml`、`logs\`、`Fwlib64.dll`。

## 日志与版本

| 项 | 位置 |
| --- | --- |
| 文件日志 | 安装目录 `logs\gateway-yyyyMMdd.log`（本地时间，含日期；约 14 天） |
| 覆盖目录 | 环境变量 `GATEWAY_LOG_DIR` |
| 控制台 | 前台运行时，时间戳带日期 |
| Windows 服务 | SCM 停启；失败可看系统事件日志 |
| 版本 | 启动第一行 `iot-daq-gateway {Version}`（含程序集版本，CI 包带 git 短 SHA） |

把当天 log 拷走即可给远程排障。日志里还应有：YAML 路径、MQTT host/port/clientId、Web 控制台监听地址、`Fwlib64.dll` 是否加载。

## 网络

| 方向 | 端口 | 用途 |
| --- | --- | --- |
| 网关 → CNC | TCP 8193（可配置） | FOCAS，仅机床网 |
| 网关 → MQTT | TCP 1883 或 8883 | 北向 |
| 浏览器 → 网关 | TCP 8080（可配置 `console.port`） | 内网 Web 配置控制台，需口令 |

不要把 8193 暴露到办公网。Windows 防火墙需放行出站 8193/1883，以及 **仅采集网** 入站 8080。示例：

```bat
netsh advfirewall firewall add rule name="IoT DAQ Gateway console" dir=in action=allow protocol=TCP localport=8080
```

机床侧检查单见 [focas.md](focas.md)。

## 升级

1. `sc stop IotDaqGateway`
2. 备份 `gateway.yaml` 与 `Fwlib64.dll`
3. 用新 zip 覆盖二进制（不要覆盖已改过的 yaml，除非发行说明要求）
4. 确认 `Fwlib64.dll` 仍在 exe 旁
5. `sc start IotDaqGateway`，核对启动日志中的版本号；浏览器仍打开 http://采集机IP:8080/

## 常见故障

| 现象 | 处理 |
| --- | --- |
| 服务立即退出 | 看 `logs\`；常见是找不到 `gateway.yaml` |
| 浏览器打不开控制台 | 看日志是否 `Web console listening`；`0.0.0.0` 无 token 会被拒绝；查防火墙 8080 |
| 口令不对 | `console.token` 或 `GATEWAY_CONSOLE_TOKEN`；示例默认 `change-me` |
| `$status=offline` 且日志含 `Fwlib64` | DLL 未放、位数不是 x64、或缺 VC 运行库（按 FANUC 说明补齐） |
| `EW_SOCKET` / 连不上 | 采集机到机床 8193 不通；选件未开 |
| MQTT 反复重连 | broker 地址/端口错；发布被丢弃，采集仍继续 |
| 两台网关互踢 | `mqtt.clientId` 重复 |
| 改了 yaml 没变化 | 未点「保存并应用」、也未重启服务 |

`fanuc.fake` 仅供开发演示，生产请用 `fanuc.focas`。本控制台只做配置，不是 SCADA / 历史库。
