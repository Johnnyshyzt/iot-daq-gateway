# IoT DAQ Gateway

Device-agnostic industrial IoT data-acquisition gateway (CNC first). Southbound V1 is Fanuc FOCAS over panel Ethernet via an adapter interface plus `FakeFanucAdapter` — no vendor FOCAS binaries in this repo. Northbound is MQTT JSON, plus HTTP push, a read-only query API, and an optional OPC UA server. See [docs/integration.md](docs/integration.md).

面向产线的开源工业物联网采集网关骨架：设备无关、先覆盖 CNC，后续可扩展注塑等机型。以普通服务或容器运行，不依赖工控机镜像或 Ladder99 base-driver。

## 特性（V1）

- **.NET 10** / `net10.0`，SDK 通过 `global.json` 固定为 **10.0.203**
- YAML 配置：网关 id、站点、MQTT、设备列表
- 采集管道：扫描周期 → 适配器采集 → `Observation` → 可选 `change_only` → MQTT
- 主题：`daq/{site}/{deviceId}/{point}` 与 `daq/{site}/{deviceId}/$status`
- `FakeFanucAdapter` 输出示例 `state` / `alarm` / `program`，无需机床
- `fanuc.focas` 在 **Windows x64** 上对 `Fwlib64.dll` 做真实 P/Invoke（库需自备，放进程旁）；缺库则 `offline`，进程不崩，后续扫描会重连
- **Windows 现场包：** 自包含 `win-x64` zip（`Host.exe`、`wwwroot`、`data/seed`、安装脚本）+ `install-service.bat` 开机自启。推送 `v*` 标签后挂到 GitHub Release；CI 的 `pack-win-x64` 仍打同一份 zip。不含 `Fwlib64.dll`
- `dotnet test` + GitHub Actions CI（无硬件 / 无厂商 DLL）
- `IProgramService` 预留 CNC 程序能力；V1 只读，功能开关默认 `false`
- **Studio 可视化：** 总览大屏（含深色全屏 `/board`）、单机详情、历史曲线、报警、稼动率与产量。统计在 Host 聚合，页面不拉原始历史。见 [docs/visualization.md](docs/visualization.md)
- **上手引导、审计和 SQLite 备份：** 总览上的五步引导、配置变更审计、管理员在「系统」页下载或恢复数据库。`GET /healthz` 不登录即可看版本和 MQTT 缓冲深度。Linux systemd 见 [docs/linux-install.md](docs/linux-install.md)
- **断线与通知：** 设备按退避重连，MQTT 断线时磁盘缓冲并按序重放，采集停顿会重启该设备工人。报警可发到企业微信、钉钉、飞书、邮件或 Webhook。见 [docs/reliability.md](docs/reliability.md) 与 [docs/notifications.md](docs/notifications.md)
- **北向合同：** 点位、状态、报警、产量和稼动有版本化 JSON Schema。MQTT 默认可保持旧字段；HTTP 推送、只读查询 API 和可选 OPC UA 服务器见 [docs/integration.md](docs/integration.md)
- 许可证 [Apache-2.0](LICENSE)

## 仓库结构

```
IotDaqGateway.sln
global.json
src/Shared/Abstractions/    契约与模型
src/Collector/              Fanuc 适配器、MQTT、采集会话
src/Api/                    管理 API（草稿 / 发布 / 回滚）
src/Host/                   唯一进程：API + 采集 + 静态页面
src/Web/                    shadcn-admin（React + Vite）
data/seed/                  首次启动复制的示例配置
tests/
configs/examples/
packaging/windows/
scripts/pack-win-x64.*
docker/
docs/
```

更多见 [docs/architecture.md](docs/architecture.md)。

## 一个 Host

采集、管理 API 和页面在同一个进程里。产品边界见 [docs/product/open-core.md](docs/product/open-core.md)。试点合同附件、销售一页和安全口径见 [docs/product/pilot-contract-appendix.md](docs/product/pilot-contract-appendix.md)、[docs/product/sales-one-pager.md](docs/product/sales-one-pager.md)、[docs/product/security-narrative.md](docs/product/security-narrative.md)。三个模块：

| 模块 | 路径 | 作用 |
| --- | --- | --- |
| Collector | `src/Collector` | Fanuc 适配器、MQTT、扫描与重载 |
| Api | `src/Api` | 草稿、校验、发布、回滚 |
| Web | `src/Web` | [shadcn-admin](https://github.com/satnaing/shadcn-admin)（MIT，见 [src/Web/README.md](src/Web/README.md)） |

`src/Host` 把三者接在一起。浏览器打开 Host，改配置，发布到数据库，同一进程采集 Fanuc 并发布 MQTT。采集留在现场机器上，以便访问机床网络。

```bash
cd src/Web && npm ci && npm run build
dotnet run --project src/Host
```

然后打开 `http://127.0.0.1:5080`。`dotnet run` 是本机演示，登录页会写明 `admin` / `admin`（以及 engineer、viewer）只适合 localhost。第一次启动在 `data/gateway.db` 创建 SQLite（可用 `HOST_DATA` 或 `STUDIO_DATA` 改数据目录）。数据库还没有配置时，会导入已有的 `data/published`、`data/draft` 或 `data/seed`；没有这些文件时写入内置默认配置。目录里有 19 个数控品牌和每品牌一份标准点位模板。之后草稿、发布和回滚只写数据库，采集读已发布槽位，并在进程内重载，不需要第二个网关进程。现场 zip 不使用这些默认口令，见 [docs/windows-install.md](docs/windows-install.md)。

目录、模拟器和数据库见 [docs/catalog/README.md](docs/catalog/README.md) 与 [docs/database.md](docs/database.md)。Studio 里可以按品牌新建模拟器设备。发布后，「总览」按车间和产线看状态，「单机」看仪表和今日状态条，「历史曲线」「报警」「稼动率」读数据库里的聚合结果。种子里的模拟器会在空库时写入约一天的状态、报警和件数。YAML / JSON 只作为导入导出。换成 PostgreSQL 时设置 `Database:Provider` 和连接字符串，见数据库文档。可视化接口见 [docs/visualization.md](docs/visualization.md)。

开发页面热更新：`cd src/Web && npm run dev`（`http://127.0.0.1:5173`，把 `/api` 代理到 5080）。CI 用 npm，本机也可以用 pnpm。

空数据库第一次启动时，可以用一份 YAML 作为导入来源。导入完成后采集仍读数据库；这份文件之后不再被监视：

```bash
dotnet run --project src/Host --no-launch-profile -- --config configs/examples/gateway.yaml
```

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
dotnet run --project src/Host --no-launch-profile -- --config configs/examples/gateway.yaml
```

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

镜像按普通运行时容器启动，不要求特权或专用工控机基础镜像。Compose 把 `configs/examples/gateway.docker.yaml` **挂到** `/app/gateway.yaml`，并把 Studio 发布到宿主机的 `http://127.0.0.1:5080`（容器内监听 `0.0.0.0:5080`，否则端口映射进不去）。命名卷 `gateway-data` 保住 `gateway.db`。容器里的数据库还是空的时候，`GATEWAY_CONFIG` 会把这份文件导入一次（Broker 主机名是 `mosquitto`）。数据库已经有已发布配置之后，改这个文件不会改变正在跑的采集；要改配置请用 Studio，或换掉数据卷里的数据库后再启动。该 Linux 镜像是 **Fake 演示**，不是生产 FOCAS 路径。systemd 安装见 [docs/linux-install.md](docs/linux-install.md)。

## 现场安装（Windows x64，无需 SDK）

生产路径是 **自包含 win-x64 zip**（内含 .NET 10 运行时、`Host.exe`、`wwwroot`、`data/seed` 和安装脚本）。工厂工控机不需要安装 SDK 10.0.203，也不需要 git 检出。页面上发布后，同一进程从数据库重载已发布配置。

1. 从 [Releases](https://github.com/Johnnyshyzt/iot-daq-gateway/releases/latest) 下载 Host 包。源码版本是 **0.7.0**，附件名是 `iot-daq-gateway-0.7.0-win-x64.zip`（`Host.exe`、Studio、`data/seed`）。已经打过标签的 [v0.4.0](https://github.com/Johnnyshyzt/iot-daq-gateway/releases/tag/v0.4.0) 仍是更早的 Host 包；推送 `v0.7.0` 之后 Release 才会换成新 zip。更早的 [v0.3.0](https://github.com/Johnnyshyzt/iot-daq-gateway/releases/tag/v0.3.0) 是只有 Gateway 的历史包（`Gateway.Host.exe`，没有 Studio），不要当成现在的 Host。还没有对应 Release 时，用 Actions 里 `pack-win-x64` 的同名 artifact，或在构建机运行 `./scripts/pack-win-x64.sh`（Windows：`powershell -File scripts/pack-win-x64.ps1`）。这些包都走同一套打包脚本，都不含 `Fwlib64.dll`
2. 解压到例如 `C:\iot-daq-gateway\`
3. 把授权的 `Fwlib64.dll` 放到与 `Host.exe` 同一目录（不进 git / 不进 zip / 不进镜像）。复制 `service.env.example` 为 `service.env` 并填写 `MQTT_USER` / `MQTT_PASSWORD`
4. 打开 `http://127.0.0.1:5080`。用 `data/auth/bootstrap-password.txt` 里的一次性密码登录并马上修改。第一次启动把 `data/seed` 导入 `data/gateway.db`，之后采集只读数据库里的已发布配置
5. 管理员运行 `install-service.bat` → 注入服务环境并让 `IotDaqGateway` 开机自启
6. 日志：`logs\gateway-yyyyMMdd.log`（启动时打印版本号）

卸载：管理员运行 `uninstall-service.bat`。完整步骤见 [docs/windows-install.md](docs/windows-install.md)。升级与备份见 [docs/ops-field.md](docs/ops-field.md)，故障对照见 [docs/field-fault-guide.md](docs/field-fault-guide.md)，无自有机床时的试点验收见 [docs/product/pilot-acceptance.md](docs/product/pilot-acceptance.md)。Fake 演示见 [docs/product/fake-demo-script.md](docs/product/fake-demo-script.md)，商业边界见 [docs/product/pricing-one-pager.md](docs/product/pricing-one-pager.md)。试点合同附件、销售一页和安全口径见 [docs/product/pilot-contract-appendix.md](docs/product/pilot-contract-appendix.md)、[docs/product/sales-one-pager.md](docs/product/sales-one-pager.md)、[docs/product/security-narrative.md](docs/product/security-narrative.md)。

维护者发版（合并发版工作流之后；合并本身不会打标签）。`X.Y.Z` 对齐 `Directory.Build.props` 的 `Version`，附件名是 `iot-daq-gateway-<version>-win-x64.zip`（没有前缀 `v`）。源码版本是 `0.7.0`：

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

推送 `v*` 后，`.github/workflows/release.yml` 检出该标签，按 CI `pack-win-x64` 的步骤打包，并创建或更新这个标签的 GitHub Release。种子配置仍是 `fanuc.fake`。先用 Fake 看页面、发布和 MQTT。真实 FOCAS 要在现场改成 `fanuc.focas` 并自备 `Fwlib64.dll`。本仓库没有发那科机床，不把端口通了写成握手成功。

**不要把 Linux Docker 当作生产 FOCAS 路径。**

## 真实 Fanuc FOCAS（Windows x64）

生产采集：Windows 采集机 + 现场 `Fwlib64.dll` + 外置 YAML。缺库或机床断开时设备为 `offline`，之后按退避重连，而不是固定间隔空转。详见 [docs/focas.md](docs/focas.md)、[docs/reliability.md](docs/reliability.md) 与 [docs/windows-install.md](docs/windows-install.md)。

## 配置要点

| 字段 | 含义 |
| --- | --- |
| `gateway.id` / `gateway.site` | 网关标识与站点，写入 MQTT 载荷与主题 |
| `pipeline.sweepInterval` | 扫描周期 |
| `pipeline.changeOnly` | 仅在点位值变化时发布（`$status` 每轮仍发） |
| `programTransfer.enabled` | `IProgramService` 开关，默认 `false` |
| `mqtt.*` | broker、clientId、QoS、可选 TLS/账号 |
| `devices[].adapter` | 品牌模拟器、开放协议或厂商 SDK 驱动，见下方驱动矩阵 |

默认采集路径是数据目录里的 `published/`（见 [docs/config/README.md](docs/config/README.md)）。`--config` 或 `GATEWAY_CONFIG` 可以改成单文件 YAML 或 v1 目录，那是无界面覆盖，不是推荐启动方式。

## FOCAS 与安全

- 厂商 FOCAS 库需用户自行提供，仓库不收录。见 [docs/focas.md](docs/focas.md)。
- 面板以太网应做网络隔离。见 [docs/security.md](docs/security.md)。

## 驱动矩阵

运行配置在数据库里。每个品牌都有模拟器。真实驱动把读数写到目录项 Id 上，`{品牌}-standard` 模板不用改地址。支持级别在 Studio「品牌目录」里按驱动显示。细节在 [docs/drivers](docs/drivers/README.md)。厂商二进制只放 `data/sdk/<厂商>/`，仓库不提交。

| 品牌 | 真实驱动 | 协议 | SDK |
| --- | --- | --- | --- |
| 发那科 | `fanuc.focas` | FOCAS2 Ethernet，8193 | `Fwlib64.dll` / `libfwlib32.so` |
| 西门子 | `siemens.opcua` | SINUMERIK OPC UA，4840 | 无。需要控制器 OPC UA 选项 |
| 三菱 | `mitsubishi.opcua` / `.mtconnect` / `.ezsocket` | OPC UA、MTConnect，或 EZSocket | EZSocket 放到 `data/sdk/mitsubishi/`，签名未核对则不调用 |
| 新代、广数、华中、凯恩帝、宝元、科德、精雕 | `*.custom` | 厂商 API | `data/sdk/<品牌>/`。没有可核对的公开签名，不调用未知入口 |
| 马扎克 Smooth/Smart、Matrix | `mazak-*.mtconnect` | MTConnect，5000 | 无 |
| 海德汉 | `heidenhain.lsv2` / `.opcua` | LSV2 19000，或 OPC UA | 无 |
| 法格 | `fagor.opcua` | OPC UA，节点由地址或浏览名给出 | 无 |
| 兄弟 | `brother.mtconnect` / `.opcua` | CNC-C00 无公开帧，专有适配器不握手 | 无 |
| 哈斯 | `haas.mtconnect` / `haas.q` | MTConnect，或 5051 Q 指令（Setting 143） | 无 |
| 西铁城、森精机 | OPC UA / MTConnect / 三菱 EZSocket | 见对应文档 | EZSocket 同上 |
| 台达 | `delta.modbus` | Modbus TCP 502 | 无。默认寄存器不是出厂表 |
| 通用 | `generic.opcua` / `.mtconnect` / `.modbus` / `.ftp` | 同上 | 无 |

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。

## 路线图

- Windows 现场包 / 开机自启 —— 已提供；Linux 官方 FOCAS 库不在本期
- 注塑等其它南向适配器
- 程序传输写路径（仍受 `IProgramService` 开关约束）
- 更多北向：HTTP 推送、只读查询和 OPC UA 服务器已在 0.7.0 提供。历史访问（OPC UA HA）和按班次补发仍未做

## License

Apache License 2.0. FANUC / FOCAS 为各自权利人商标；本项目不提供其原生 SDK。
