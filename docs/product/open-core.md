# Open Core 产品架构

M1 里 **Edge Runtime** 和 **Config Studio** 都在这个仓库，许可证都是 Apache-2.0。Runtime 在 `src/`，Studio 在 [`studio/`](../../studio/README.md)。以后如果要拆成独立的 `iot-daq-studio` 仓库，可以再搬；那是后续选择，不是现在的布局。

M1 已锁定的边界：

| 决定 | 含义 |
| --- | --- |
| Open Core | Runtime 与 Studio 都在本仓库开源。可选的后续拆仓不改变当前事实源 |
| 同机部署 | 当前是同机 sidecar：`Studio.Host` 管页面和配置 API，`Gateway.Host` 管采集。同一进程嵌入留到以后，接缝是 `MapStudioApi()` |
| 文件为源 | 配置以 YAML 为单一事实源。数据库只能做可选缓存，不能成为唯一副本 |
| 设备 | M1 只做透 Fanuc：`fanuc.fake` 与 `fanuc.focas`。其他品牌不进 schema |
| 北向 | M1 只有 MQTT JSON。OPC UA 明确留到后续 |

## 运行时与 Studio

```
┌─────────────────────────────────────────────┐
│  Config Studio（studio/，本仓库）            │
│  Vue SPA + Studio.Host /api/v1              │
└──────────────────┬──────────────────────────┘
                   │ studio/data/published
                   │ 127.0.0.1:5081 状态与重载
┌──────────────────▼──────────────────────────┐
│  Gateway.Host                               │
│  Abstractions → Adapters(Fanuc) → MQTT Sink │
│  AcquisitionWorker, change_only, health     │
└──────────────────┬──────────────────────────┘
                   │ MQTT JSON
            ┌──────▼──────┐
            │  Broker     │ → 用户 TSDB / SCADA / MES
            └─────────────┘
```

配置目录必须是同一份已发布树：

1. **同机 sidecar（当前）**：Studio 监听 `127.0.0.1:5080`，把发布结果写到 `studio/data/published`。网关用 `--config` 指向这个目录。发布后 Studio 调用网关回环上的 `POST /api/v1/runtime/reload`；网关也会监视该目录。运行态页面优先读 `http://127.0.0.1:5081`，网关没启动时退回模拟数据。
2. **同进程（以后）**：`Gateway.Host` 继续跑采集，并在同一进程挂上 `MapStudioApi()` 和静态页。现在不要把它当成已经接上。

一起启动的步骤见 [studio/README.md](../../studio/README.md)。M3 的 Cloud / Fleet（远程下发多台网关）不在本期。

## 仓库里有什么

| 已在本仓库 | 还没做，也不在 M1 |
| --- | --- |
| 采集运行时、`FakeFanucAdapter`、FOCAS 桩（无厂商二进制） | 许可证签发、安装器 |
| Studio SPA、草稿 / 发布 / 回滚、中文页面 | 同进程嵌入 |
| YAML 契约、JSON Schema、`configs/examples/v1/` | OPC UA、其他品牌适配器 |
| 单文件 YAML 快速开始，以及 v1 目录加载 | Fleet 云端 |
| 网关回环状态 API（只绑定本机） | |

点位页可以导入导出 CSV。Excel 另存为 CSV 后再导入。厂商 `Fwlib64.dll` 不入库、不进镜像。现场放置方式见 [focas.md](../focas.md)。

## 当前运行时与 v1 契约

`Gateway.Host` 仍然支持单文件 YAML（`--config <file>` 或 `GATEWAY_CONFIG`）。未指定路径时默认仍是 [configs/examples/gateway.yaml](../../configs/examples/gateway.yaml)，Fake + MQTT 快速开始不变。

`--config` 也可以指向 v1 目录，或指向该目录里的 `gateway.yaml`。加载器看到 `apiVersion: daq.gateway/v1` 且 `kind: Gateway` 时，读取同目录的 `devices/`、`points/`、`sinks/mqtt.yaml`。示例在 [configs/examples/v1/](../../configs/examples/v1/)。Studio 发布出的树与这套布局相同，网关读的是 `studio/data/published`，不读草稿。

字段对应关系写在 [配置目录说明](../config/README.md)。

## 配置与发布

```
Studio 编辑草稿 (studio/data/draft)
        │  POST /api/v1/config/validate
        ▼
   校验通过后 POST /api/v1/config/publish
        │  Studio CanonicalRevision → revisions/<hash>
        ▼
   studio/data/published   ← Gateway.Host 读取这一份
        │  POST 127.0.0.1:5081/api/v1/runtime/reload
        │  目录监视作为备份
        └─ POST /api/v1/config/rollback 回到某一 hash
```

重载失败时保留上一份正在采集的会话，进程不退出。这与缺 FOCAS 库时设备 `offline`、进程不崩的行为一致。

敏感项只写环境变量名（`usernameFromEnv` / `passwordFromEnv`），明文密码不进配置文件，也不进 revision。环境变量没设置时用户名和密码留空，加载不因此失败。

## 许可证桩

Runtime 读取 YAML 并采集时不检查许可证。Studio 的 `GET /api/v1/license` 返回未强制校验，管理 API 也不会因为缺少许可证而拒绝。许可证文件不参与 revision。

## 配置接口

`Gateway.Abstractions` 里的 `IConfigStore` / `IConfigPublisher` 是草稿与发布的边界草图。M1 真正写文件的是 `Studio.Host` 的 `ConfigStore`，它还没有实现这两个接口。`Gateway.Host` 不注册它们，采集循环直接读已发布 YAML。

| 接口 | 职责 |
| --- | --- |
| `IConfigStore` | 读写草稿 YAML 文档；查询已发布 revision |
| `IConfigPublisher` | 校验草稿、发布为内容寻址 revision、按 hash 回滚 |

字段级约束以 JSON Schema 为准。Studio 另外有自己的文档模型，用来序列化 YAML。

## 明确不做（M1）

OPC UA、Fanuc 以外的适配器、Fleet 云端、组态大屏、长期时序库、CNC 程序写入实开。程序相关能力仍是 `IProgramService` 只读预留，`programWrite` 默认 `false`。
