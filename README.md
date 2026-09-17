# IoT DAQ Gateway

Device-agnostic industrial IoT data-acquisition gateway (CNC first). Southbound V1 is Fanuc FOCAS over panel Ethernet via an adapter interface plus `FakeFanucAdapter` — no vendor FOCAS binaries in this repo. Northbound V1 is MQTT JSON.

面向产线的开源工业物联网采集网关骨架：设备无关、先覆盖 CNC，后续可扩展注塑等机型。以普通服务或容器运行，不依赖工控机镜像或 Ladder99 base-driver。

## 特性（V1）

- **.NET 10** / `net10.0`，SDK 通过 `global.json` 固定为 **10.0.203**
- YAML 配置：网关 id、站点、MQTT、设备列表；**内置中文 Web 控制台**（默认 8080）可添加多台设备并写回 YAML，不必重新打包
- 采集管道：扫描周期 → 适配器采集 → `Observation` → 可选 `change_only` → MQTT
- 主题：`daq/{site}/{deviceId}/{point}` 与 `daq/{site}/{deviceId}/$status`
- `FakeFanucAdapter` 输出示例 `state` / `alarm` / `program`，无需机床
- `fanuc.focas` 在 **Windows x64** 上对 `Fwlib64.dll` 做真实 P/Invoke（库需自备，放进程旁）；缺库则 `offline`，进程不崩，后续扫描会重连
- **Windows 现场包：** 自包含 `win-x64` zip + 外置 YAML + `install-service.bat` 开机自启；CI 在 Windows 上打包
- `dotnet test` + GitHub Actions CI（无硬件 / 无厂商 DLL）
- `IProgramService` 预留 CNC 程序能力；V1 只读，功能开关默认 `false`
- 许可证 [Apache-2.0](LICENSE)

## 仓库结构

```
IotDaqGateway.sln
global.json
src/Gateway.Abstractions/   契约与模型
src/Gateway.Host/           宿主、YAML、扫描循环、Web 配置控制台
src/Adapters.Fanuc/         Fake + Windows FOCAS（Fwlib64 P/Invoke）
src/Sinks.Mqtt/             MQTTnet JSON
tests/Gateway.Tests/        无硬件回归（缺 DLL / YAML）
configs/examples/
packaging/windows/        服务安装脚本与现场说明（打进 zip）
scripts/pack-win-x64.*   自包含 win-x64 打包
docker/
docs/
```

更多见 [docs/architecture.md](docs/architecture.md)。

## 环境

- **现场采集机：** 不需要 SDK。使用自包含 Windows 包，见下方「现场安装」。
- **构建 / 开发：** [.NET SDK 10.0.203](https://dotnet.microsoft.com/download/dotnet/10.0)（`global.json` `rollForward: disable`）
- 可选：MQTT broker（Mosquitto 等），用于真正看到报文

```bash
dotnet --list-sdks   # 需包含 10.0.203
```

## 构建

```bash
dotnet build IotDaqGateway.sln
dotnet test IotDaqGateway.sln

# 现场 zip（自包含 win-x64，不含 Fwlib64.dll）
./scripts/pack-win-x64.sh
```

## 运行（Fake + MQTT）

**第一路径：打开 Web 控制台配置，不必手改 YAML。**

1. 启动 broker（任选其一）：

```bash
# 本机 Mosquitto
mosquitto -c docker/mosquitto.conf

# 或 compose 只起 broker
docker compose -f docker/docker-compose.yml up mosquitto
```

2. 订阅主题（另开终端）：

```bash
mosquitto_sub -h 127.0.0.1 -t 'daq/#' -v
```

3. 启动网关：

```bash
dotnet run --project src/Gateway.Host --no-launch-profile -- --config configs/examples/gateway.yaml
```

4. 浏览器打开 **http://127.0.0.1:8080/** （示例配置绑在本机回环，无口令）。内网部署把 `console.bind` 设为 `0.0.0.0` 并设置 `console.token`（或环境变量 `GATEWAY_CONSOLE_TOKEN`），然后打开 `http://采集机IP:8080/`。在页面添加 `fanuc.fake` / `fanuc.focas` 设备，点「保存并应用」。

`--config` 会从当前目录向上查找，因此即使 `dotnet run` 的工作目录是项目文件夹，仓库根下的示例路径仍然有效。

未启动 broker 时进程仍会运行：采集继续，MQTT 连接失败会打日志并丢弃当次发布。

示例报文：

- `daq/plant-a/cnc-01/state` — `IDLE` / `RUNNING` / `ALARM`（按分钟内相位变化，便于验证 `change_only`）
- `daq/plant-a/cnc-01/alarm` — `0` 或 `100`
- `daq/plant-a/cnc-01/program` — `O0001`
- `daq/plant-a/cnc-01/$status` — 每轮扫描的在线状态

## Docker

```bash
docker compose -f docker/docker-compose.yml up --build
```

镜像按普通运行时容器启动，不要求特权或专用工控机基础镜像。Compose 把 `configs/examples/gateway.docker.yaml` **挂到** `/app/gateway.yaml`（可写），并发布 **8080**。浏览器打开 http://127.0.0.1:8080/ ，口令 `change-me`（或 `GATEWAY_CONSOLE_TOKEN`）。该 Linux 镜像是 **Fake 演示**，不是生产 FOCAS 路径。

## 现场安装（Windows x64，无需 SDK）

生产路径是 **自包含 win-x64 zip**（内含 .NET 10 运行时）。工厂工控机不需要安装 SDK 10.0.203，也不需要 git 检出。**先打开浏览器配置**（http://采集机IP:8080/），不必手改 YAML；保存会写回安装目录的 `gateway.yaml` 并热加载采集。

1. 从 GitHub Actions 的 `pack-win-x64` 产物（或 `./scripts/pack-win-x64.sh` / `scripts/pack-win-x64.ps1`）取得 `iot-daq-gateway-*-win-x64.zip`
2. 解压到例如 `C:\iot-daq-gateway\`
3. 把授权的 `Fwlib64.dll` 放到与 `Gateway.Host.exe` 同一目录（不进 git / 不进镜像）
4. 运行网关后用浏览器打开 http://127.0.0.1:8080/ 或 http://采集机IP:8080/ ，用 `console.token` 登录，添加机床 / MQTT
5. 管理员运行 `install-service.bat` → 服务 `IotDaqGateway` 开机自启
6. 日志：`logs\gateway-yyyyMMdd.log`（启动时打印版本号与控制台地址）

卸载：管理员运行 `uninstall-service.bat`。完整步骤见 [docs/windows-install.md](docs/windows-install.md)。

**不要把 Linux Docker 当作生产 FOCAS 路径。**

## 真实 Fanuc FOCAS（Windows x64）

生产采集：Windows 采集机 + 现场 `Fwlib64.dll` + 外置 YAML。缺库或机床断开时设备为 `offline`，之后每轮扫描会重连。详见 [docs/focas.md](docs/focas.md) 与 [docs/windows-install.md](docs/windows-install.md)。

## 配置要点

| 字段 | 含义 |
| --- | --- |
| `gateway.id` / `gateway.site` | 网关标识与站点，写入 MQTT 载荷与主题 |
| `pipeline.sweepInterval` | 扫描周期 |
| `pipeline.changeOnly` | 仅在点位值变化时发布（`$status` 每轮仍发） |
| `programTransfer.enabled` | `IProgramService` 开关，默认 `false` |
| `mqtt.*` | broker、clientId、QoS、可选 TLS/账号 |
| `devices[].adapter` | `fanuc.fake` 或 `fanuc.focas` |
| `console.bind` / `port` / `token` | 内置 Web 控制台。默认端口 8080。绑 `0.0.0.0` 时必须有 token（或 `GATEWAY_CONSOLE_TOKEN`） |

配置路径：`--config <file>` 或环境变量 `GATEWAY_CONFIG`。未指定时依次尝试当前目录 `gateway.yaml`、`configs/examples/gateway.yaml`、输出目录内副本。Web 控制台把变更写回该文件。

## FOCAS 与安全

- 厂商 FOCAS 库需用户自行提供，仓库不收录。见 [docs/focas.md](docs/focas.md)。
- 面板以太网应做网络隔离。见 [docs/security.md](docs/security.md)。

## 路线图

- Windows 现场包 / 开机自启 —— 已提供；Linux 官方 FOCAS 库不在本期
- 注塑等其它南向适配器
- 程序传输写路径（仍受 `IProgramService` 开关约束）
- 更多北向（当前仅 MQTT JSON）

## License

Apache License 2.0. FANUC / FOCAS 为各自权利人商标；本项目不提供其原生 SDK。
