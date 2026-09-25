# Open Core 产品架构

本仓库是开源 **Edge Runtime** 与配置契约。可视化 **Config Studio** 在独立商业仓库中实现，不在这里写 UI。

M1 已锁定的边界：

| 决定 | 含义 |
| --- | --- |
| Open Core | 本仓库 = Edge Runtime + 文件契约 + Management API 合同；Studio 另仓 |
| 同机嵌入 | M1 Studio 与网关在同一台机器。优先同一进程托管静态前端和 Management API，同机 sidecar 也可以 |
| 文件为源 | 配置以 YAML 为单一事实源。数据库只能做可选缓存，不能成为唯一副本 |
| 设备 | M1 只做透 Fanuc：`fanuc.fake` 与 `fanuc.focas`。其他品牌不进 schema |
| 北向 | 商业 V1 只有 MQTT JSON。OPC UA 明确留到后续 |

## 运行时与 Studio

```
┌─────────────────────────────────────────────┐
│  Config Studio (commercial, embedded M1)    │
│  SPA + Management API + license gate        │
└──────────────────┬──────────────────────────┘
                   │ files + /api/v1
┌──────────────────▼──────────────────────────┐
│  Edge Runtime (this open-source repo)       │
│  Abstractions → Adapters(Fanuc) → MQTT Sink │
│  AcquisitionWorker, change_only, health     │
└──────────────────┬──────────────────────────┘
                   │ MQTT JSON
            ┌──────▼──────┐
            │  Broker     │ → 用户 TSDB / SCADA / MES
            └─────────────┘
```

同机部署有两种等价形态，配置目录必须是同一份：

1. **同进程（优先）**：`Gateway.Host` 继续跑采集；商业静态文件与 Management API 嵌在同一进程，浏览器与 API 同源。
2. **同机 sidecar**：Studio 进程只绑定本机回环地址，与 Runtime 共享 `config/` 工作区。

M3 的 Cloud / Fleet（远程下发多台网关）不在本期。

## 仓库边界

| 放在本仓库（Apache-2.0） | 放在商业仓库 |
| --- | --- |
| 采集运行时、`FakeFanucAdapter`、FOCAS 桩（无厂商二进制） | Studio SPA、页面与交互 |
| YAML 契约、JSON Schema、`configs/examples/v1/` | 点位 Excel 导入导出、点位模板 |
| Management API 合同（[management-api.md](../api/management-api.md)） | 许可证签发、安装器 |
| `IConfigStore` / `IConfigPublisher` 接口（尚无 Host 实现） | RBAC 用户管理与审计 UI |
| 手写 YAML 即可运行的现有单文件配置 | 可视化编辑、校验、发布 |

厂商 `Fwlib64.dll` 不入库、不进镜像。现场放置方式见 [focas.md](../focas.md)。

## 当前运行时与 v1 契约

今天的 Host 读取**一份** YAML（`--config` / `GATEWAY_CONFIG`），形状见 `configs/examples/gateway.yaml`：`gateway`、`pipeline`、`mqtt`、`devices`。Fake + MQTT 快速开始保持不变。

Studio 与后续加载器使用另一套 **v1 多文件契约**（`apiVersion: daq.gateway/v1`）。示例在 `configs/examples/v1/`，字段由 `schemas/` 约束。Host 仍不会加载该目录；采集行为以现有单文件配置为准，直到加载器接上 `published/`。

两边的对应关系写在 [配置目录说明](../config/README.md)。

## 配置与发布

```
Studio 编辑草稿 (config/draft)
        │  POST /api/v1/config/validate
        ▼
   校验通过后 POST /api/v1/config/publish
        │  规范 JSON → sha256 → config/revisions/<hash>
        ▼
   config/published   ← Runtime 将来只读这一份
        │
        └─ POST /api/v1/config/rollback 回到某一 hash
```

发布后的生效方式：优先热加载 `published/`；热加载失败时，用上一份成功发布的 revision 安全重启采集循环，进程保持运行。这与今天缺 FOCAS 库时设备 `offline`、进程不退出的行为一致。

敏感项只写环境变量名（`usernameFromEnv` / `passwordFromEnv`），明文密码不进配置文件，也不进 revision。

## 许可证桩

开源 Runtime 读取 YAML 并采集时不检查许可证。Management API（商业面）需要许可证文件，路径为 `IOT_DAQ_LICENSE` 或工作区根的 `license.json`。文件不参与 revision。M1 可以只检查文件存在且含非空 `id`。缺少许可证时 API 返回 `license_required`，采集继续。

## 开源接口

`Gateway.Abstractions` 增加两个边界接口，供以后的 Management API 实现调用。Host **没有**注册实现，现有扫描循环不经过它们。

| 接口 | 职责 |
| --- | --- |
| `IConfigStore` | 读写草稿 YAML 文档；查询已发布 revision |
| `IConfigPublisher` | 校验草稿、发布为内容寻址 revision、按 hash 回滚 |

字段级约束以 JSON Schema 为准，不在 C# 里再维护一套平行模型。

## 明确不做（M1）

OPC UA、Fanuc 以外的适配器、Fleet 云端、组态大屏、长期时序库、CNC 程序写入实开。程序相关能力仍是 `IProgramService` 只读预留，`programWrite` 默认 `false`。
