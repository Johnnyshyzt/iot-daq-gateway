# Windows 内网安装（生产路径）

第一期生产采集路径是 **Windows x64 采集机**：自包含 zip + 现场自备 `Fwlib64.dll` + 外置 `gateway.yaml` + Windows 服务开机自启。

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
2. 编辑 `gateway.yaml`（包内已是 FOCAS 示例）：
   - `devices[].options.host` / `port`：机床面板 IP，常见端口 **8193**
   - `mqtt.host` / `port` / **唯一** `mqtt.clientId`（两台网关不要相同）
3. 将授权的 **64 位** `Fwlib64.dll` 放到与 `Gateway.Host.exe` 同一目录。仓库和镜像从不附带该文件。缺库时进程仍运行，设备 `$status=offline`。
4. **建议先前台验证**：双击 `run-console.bat`，看 `logs\gateway-yyyyMMdd.log` 是否打印版本号、配置路径、`Fwlib64.dll` 加载结果。
5. 确认后 **以管理员身份** 运行 `install-service.bat`：
   - 注册服务名 `IotDaqGateway`（显示名 IoT DAQ Gateway）
   - `start= auto`，开机自启
   - 失败后自动重启
6. 验收：订阅 `daq/#`，看到真实 `$status=online` 以及随机床变化的 `state`（不是 Fake 的 60 秒相位）。

手动等价命令（管理员 cmd）：

```bat
sc create IotDaqGateway binPath= "\"C:\iot-daq-gateway\Gateway.Host.exe\" --config \"C:\iot-daq-gateway\gateway.yaml\"" start= auto DisplayName= "IoT DAQ Gateway"
sc failure IotDaqGateway reset= 86400 actions= restart/5000/restart/10000/restart/30000
sc start IotDaqGateway
sc query IotDaqGateway
```

## 改配置（不必重建）

YAML 只在启动时读取一次。改机床 IP / MQTT 后：

```bat
sc stop IotDaqGateway
sc start IotDaqGateway
```

或「服务」管理器里重启 **IoT DAQ Gateway**。

配置查找顺序：`--config`（安装脚本已传入发布目录下的 `gateway.yaml`）、环境变量 `GATEWAY_CONFIG`、进程目录 `gateway.yaml`。服务的工作目录可能是 `C:\Windows\System32`，因此安装脚本始终传绝对路径。

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

把当天 log 拷走即可给远程排障。日志里还应有：YAML 路径、MQTT host/port/clientId、`Fwlib64.dll` 是否加载。

## 网络

| 方向 | 端口 | 用途 |
| --- | --- | --- |
| 网关 → CNC | TCP 8193（可配置） | FOCAS，仅机床网 |
| 网关 → MQTT | TCP 1883 或 8883 | 北向 |
| 网关入站 | 无 | 无管理 HTTP 口 |

不要把 8193 暴露到办公网。Windows 防火墙只需放行上述出站。机床侧检查单见 [focas.md](focas.md)。

## 升级

1. `sc stop IotDaqGateway`
2. 备份 `gateway.yaml` 与 `Fwlib64.dll`
3. 用新 zip 覆盖二进制（不要覆盖已改过的 yaml，除非发行说明要求）
4. 确认 `Fwlib64.dll` 仍在 exe 旁
5. `sc start IotDaqGateway`，核对启动日志中的版本号

## 常见故障

| 现象 | 处理 |
| --- | --- |
| 服务立即退出 | 看 `logs\`；常见是找不到 `gateway.yaml` |
| `$status=offline` 且日志含 `Fwlib64` | DLL 未放、位数不是 x64、或缺 VC 运行库（按 FANUC 说明补齐） |
| `EW_SOCKET` / 连不上 | 采集机到机床 8193 不通；选件未开 |
| MQTT 反复重连 | broker 地址/端口错；发布被丢弃，采集仍继续 |
| 两台网关互踢 | `mqtt.clientId` 重复 |
| 改了 yaml 没变化 | 未重启服务；配置不热加载 |

`fanuc.fake` 仅供开发演示，生产请用 `fanuc.focas`。
