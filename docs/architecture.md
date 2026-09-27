# 架构说明 / Architecture

设备无关的采集网关：南向适配器采集，北向 MQTT 发布。抽象层独立设计，不依赖、不 fork Ladder99 base-driver。

```
docs/catalog/cnc-catalog.json
    │  首次启动写入 SQLite（或 PostgreSQL）
    ▼
brands / items / adapters / templates / devices
    │
    ▼
ISouthboundAdapterFactory
    fanuc.fake | fanuc.focas | {brand}.sim | 第二阶段协议桩
    │
    ▼
AcquisitionWorker
    adapter.Collect() → Observation
    断线：指数退避；停顿：看门狗重建该设备适配器
    ISampleWriter → sample_latest + sample_history
    optional change_only → MQTT JSON
    Broker 不可达 → mqtt-spool/ 按序重放
    报警 → 通知通道（企业微信 / 钉钉 / 飞书 / SMTP / Webhook）
```

| 项目 | 职责 |
| --- | --- |
| `src/Shared/Abstractions` | 模型、适配器/下沉接口、`IProgramService`、`ISampleWriter` |
| `src/Shared/Catalog` | 嵌入的 19 品牌目录 |
| `src/Persistence` | EF Core：SQLite 默认，PostgreSQL 可选 |
| `src/Collector` | Fanuc、品牌模拟器、第二阶段驱动桩、MQTT、扫描循环 |
| `src/Api` | 草稿、校验、发布、回滚、目录与采样查询 |
| `src/Host` | 唯一可执行文件：Api、Collector、Web、数据库 |
| `src/Web` | shadcn-admin（React + Vite）。MIT 归属见 [src/Web/README.md](../src/Web/README.md) |

数据库、保留策略和切换 PostgreSQL 见 [database.md](database.md)。目录来源和同义词规则见 [catalog/README.md](catalog/README.md)。总览、历史曲线、报警和稼动率见 [visualization.md](visualization.md)。断线重连、MQTT 缓冲和看门狗见 [reliability.md](reliability.md)。报警通知和稼动报表见 [notifications.md](notifications.md)。

`fanuc.fake` 与 `fanuc.focas` 保持原行为。每个品牌另有 `{brand}.sim`，只产出该品牌目录中的点。真实协议类（OPC UA、MTConnect、LSV2、FTP 等）在 `Adapters.Cnc` 里是不连接、不附带厂商 SDK 的桩，发布时给出警告，设备保持离线。

`dotnet run --project src/Host` 同时提供页面、`/api/v1` 和采集。发布或回滚后，Api 直接调用进程内的 `ICollectorControl.TryReloadAsync`，采集改读数据库里的已发布配置。运行态页在采集启动时为 `live`；测试或 `Host:Acquisition=off` 时退回 `mock`。`--config` / `GATEWAY_CONFIG` 只在数据库还没有已发布配置时导入一次，不是运行中的覆盖路径。

后续机型（注塑等）新增 `ISouthboundAdapter` 实现并注册 factory，无需改北向契约。产品边界见 [docs/product/open-core.md](product/open-core.md)。

生产采集按 **Windows x64 自包含进程 / Windows 服务** 运行；Linux 容器只承担 Fake 演示。不假设工控机特权或专用驱动盘。现场安装见 [windows-install.md](windows-install.md)。
