# 架构说明 / Architecture

设备无关的采集网关：南向适配器采集，北向 MQTT 发布。抽象层独立设计，不依赖、不 fork Ladder99 base-driver。

```
devices (YAML)
    │
    ▼
ISouthboundAdapterFactory  ── fanuc.fake | fanuc.focas
    │
    ▼
AcquisitionWorker
    sweep interval
    adapter.Collect() → Observation
    optional change_only
    INorthboundSink (MQTT JSON)
    daq/{site}/{deviceId}/{point}
    daq/{site}/{deviceId}/$status
```

| 项目 | 职责 |
| --- | --- |
| `src/Shared/Abstractions` | 模型、适配器/下沉接口、`IProgramService`、主题规则 |
| `src/Collector` | Fanuc 适配器、MQTT、扫描循环、change_only、可重载会话 |
| `src/Api` | 草稿、校验、发布、回滚、运行态查询 |
| `src/Host` | 唯一可执行文件：在一个进程里接上 Api、Collector，并托管 Web |
| `src/Web` | shadcn-admin（React + Vite）。MIT 归属见 [src/Web/README.md](../src/Web/README.md) |

`dotnet run --project src/Host` 同时提供页面、`/api/v1` 和采集。发布或回滚后，Api 直接调用进程内的 `ICollectorControl.TryReloadAsync`，采集改读 `data/published`。运行态页在采集启动时为 `live`；测试或 `Host:Acquisition=off` 时退回 `mock`。`--config` / `GATEWAY_CONFIG` 只作为无界面覆盖，不是主路径。

后续机型（注塑等）新增 `ISouthboundAdapter` 实现并注册 factory，无需改北向契约。产品边界见 [docs/product/open-core.md](product/open-core.md)。

生产采集按 **Windows x64 自包含进程 / Windows 服务** 运行；Linux 容器只承担 Fake 演示。不假设工控机特权或专用驱动盘。现场安装见 [windows-install.md](windows-install.md)。
