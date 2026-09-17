# IoT DAQ Gateway

Device-agnostic industrial IoT data-acquisition gateway (CNC first). Southbound V1 is Fanuc FOCAS over panel Ethernet via an adapter interface plus `FakeFanucAdapter` — no vendor FOCAS binaries in this repo. Northbound V1 is MQTT JSON.

面向产线的开源工业物联网采集网关骨架：设备无关、先覆盖 CNC，后续可扩展注塑等机型。以普通服务或容器运行，不依赖工控机镜像或 Ladder99 base-driver。

## 特性（V1）

- **.NET 10** / `net10.0`，SDK 通过 `global.json` 固定为 **10.0.203**
- YAML 配置：网关 id、站点、MQTT、设备列表
- 采集管道：扫描周期 → 适配器采集 → `Observation` → 可选 `change_only` → MQTT
- 主题：`daq/{site}/{deviceId}/{point}` 与 `daq/{site}/{deviceId}/$status`
- `FakeFanucAdapter` 输出示例 `state` / `alarm` / `program`，无需机床
- `fanuc.focas` 走同一接口，P/Invoke 为 TODO 桩
- `IProgramService` 预留 CNC 程序能力；V1 只读，功能开关默认 `false`
- 许可证 [Apache-2.0](LICENSE)

## 仓库结构

```
IotDaqGateway.sln
global.json
src/Gateway.Abstractions/   契约与模型
src/Gateway.Host/           宿主、YAML、扫描循环
src/Adapters.Fanuc/         Fake + FOCAS 桩
src/Sinks.Mqtt/             MQTTnet JSON
configs/examples/
docker/
docs/
```

更多见 [docs/architecture.md](docs/architecture.md)。

## 环境

- [.NET SDK 10.0.203](https://dotnet.microsoft.com/download/dotnet/10.0)
- 可选：MQTT broker（Mosquitto 等），用于真正看到报文

```bash
dotnet --list-sdks   # 需包含 10.0.203
```

## 构建

```bash
dotnet build IotDaqGateway.sln
```

## 运行（Fake + MQTT）

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
dotnet run --project src/Gateway.Host -- --config configs/examples/gateway.yaml
```

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

镜像按普通运行时容器启动，不要求特权或专用工控机基础镜像。Compose 使用 `configs/examples/gateway.docker.yaml`（broker 主机名为 `mosquitto`）。

## 配置要点

| 字段 | 含义 |
| --- | --- |
| `gateway.id` / `gateway.site` | 网关标识与站点，写入 MQTT 载荷与主题 |
| `pipeline.sweepInterval` | 扫描周期 |
| `pipeline.changeOnly` | 仅在点位值变化时发布（`$status` 每轮仍发） |
| `programTransfer.enabled` | `IProgramService` 开关，默认 `false` |
| `mqtt.*` | broker、clientId、QoS、可选 TLS/账号 |
| `devices[].adapter` | `fanuc.fake` 或 `fanuc.focas` |

配置路径：`--config <file>` 或环境变量 `GATEWAY_CONFIG`。未指定时依次尝试当前目录 `gateway.yaml`、`configs/examples/gateway.yaml`、输出目录内副本。

## FOCAS 与安全

- 厂商 FOCAS 库需用户自行提供，仓库不收录。见 [docs/focas.md](docs/focas.md)。
- 面板以太网应做网络隔离。见 [docs/security.md](docs/security.md)。

## 路线图

- 绑定真实 FOCAS P/Invoke（用户提供库）
- 注塑等其它南向适配器
- 程序传输写路径（仍受 `IProgramService` 开关约束）
- 更多北向（当前仅 MQTT JSON）

## License

Apache License 2.0. FANUC / FOCAS 为各自权利人商标；本项目不提供其原生 SDK。
