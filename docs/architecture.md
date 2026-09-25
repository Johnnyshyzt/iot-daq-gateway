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
| `Gateway.Abstractions` | 模型、适配器/下沉接口、`IProgramService`、主题规则 |
| `Gateway.Host` | YAML 配置、扫描循环、change_only、组合根 |
| `Adapters.Fanuc` | Fake 适配器 + Windows `Fwlib64.dll` FOCAS P/Invoke（库不入库） |
| `Sinks.Mqtt` | MQTTnet JSON 发布 |
| `studio/` | Config Studio：草稿、发布、页面。与网关同机 sidecar，见 [studio/README.md](../studio/README.md) |

`Gateway.Host` 启动时加载 `--config` 指向的单文件 YAML，或 v1 目录（`configs/examples/v1`、`studio/data/published`）。采集会话可以在不退出进程的情况下重载。本机 `127.0.0.1:5081` 提供状态、观测、日志尾和 `POST /api/v1/runtime/reload`。Studio 页面在网关未启动时使用模拟运行态。

后续机型（注塑等）新增 `ISouthboundAdapter` 实现并注册 factory，无需改北向契约。产品边界见 [docs/product/open-core.md](product/open-core.md)。

生产采集按 **Windows x64 自包含进程 / Windows 服务** 运行；Linux 容器只承担 Fake 演示。不假设工控机特权或专用驱动盘。现场安装见 [windows-install.md](windows-install.md)。
