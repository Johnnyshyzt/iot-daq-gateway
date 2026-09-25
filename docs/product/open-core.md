# Open Core 产品架构

M1 里采集、管理 API 和页面都在这个仓库，许可证都是 Apache-2.0。现场只部署一个 Host 进程。以后如果要拆仓，可以再搬；那是后续选择，不是现在的布局。

M1 已锁定的边界：

| 决定 | 含义 |
| --- | --- |
| Open Core | 三个模块都在本仓库开源。可选的后续拆仓不改变当前事实源 |
| 一个进程 | `src/Host` 在进程内运行 Api 与 Collector，并托管 `src/Web` 的静态构建。浏览器打开 Host 地址即可配置并采集 |
| 文件为源 | 配置以 YAML 为单一事实源。数据库只能做可选缓存，不能成为唯一副本 |
| 设备 | M1 只做透 Fanuc：`fanuc.fake` 与 `fanuc.focas`。其他品牌不进 schema |
| 北向 | M1 只有 MQTT JSON。OPC UA 明确留到后续 |
| 现场 | 采集必须跑在能访问机床的机器上，不是只放在云上的 API |

无自有发那科机床时，销售和试点按 [pilot-acceptance.md](pilot-acceptance.md)：出发前走通 Fake，FOCAS 真机留到客户现场，第一次联调单列实施人天。约 10 分钟演示见 [fake-demo-script.md](fake-demo-script.md)。商业边界见 [pricing-one-pager.md](pricing-one-pager.md)，价格数字不在本文。试点合同附件、销售一页和安全口径见 [pilot-contract-appendix.md](pilot-contract-appendix.md)、[sales-one-pager.md](sales-one-pager.md)、[security-narrative.md](security-narrative.md)。

## 三个模块，一个 Host

```
浏览器
   │  http://127.0.0.1:5080
   ▼
┌──────────────────────────────────────────────┐
│ Host                                         │
│  Web   shadcn-admin 静态页                    │
│  Api   /api/v1 草稿、发布、回滚               │
│  Collector  Fanuc → MQTT                     │
│       发布后进程内重载 data/published         │
└────────────────────┬─────────────────────────┘
                     │ MQTT JSON
              ┌──────▼──────┐
              │  Broker     │ → 用户 TSDB / SCADA / MES
              └─────────────┘
```

| 模块 | 路径 |
| --- | --- |
| Collector | `src/Collector`（适配器、MQTT、采集会话） |
| Api | `src/Api`（管理 API 库） |
| Web | `src/Web`（shadcn-admin，MIT 归属见该目录 README） |
| Host | `src/Host`（唯一可执行文件） |

启动：

```bash
cd src/Web && npm ci && npm run build
dotnet run --project src/Host
```

数据目录默认是仓库 `data/`（`HOST_DATA` 或 `STUDIO_DATA` 可改）。发布写入 `data/published`，同一进程立刻重载采集；目录监视只作为磁盘改动的备份。采集关掉时（`Host:Acquisition=off`）运行态退回模拟数据。`--config` 或 `GATEWAY_CONFIG` 会改采集所读的文件，那是无界面覆盖，主路径不要用。

M3 的 Cloud / Fleet（远程下发多台网关）不在本期。

## 仓库里有什么

| 已在本仓库 | 还没做，也不在 M1 |
| --- | --- |
| 采集运行时、`FakeFanucAdapter`、FOCAS 桩（无厂商二进制） | 许可证签发、安装器 |
| shadcn-admin 页面、草稿 / 发布 / 回滚、中文导航 | OPC UA、其他品牌适配器 |
| YAML 契约、JSON Schema、`configs/examples/v1/` | Fleet 云端 |
| 一个 Host：页面、管理 API、进程内重载采集 | |

点位页可以导入导出 CSV。Excel 另存为 CSV 后再导入。厂商 `Fwlib64.dll` 不入库、不进镜像。现场放置方式见 [focas.md](../focas.md)。

## 当前运行时与 v1 契约

Host 默认采集 `data/published`。那棵树与 [configs/examples/v1/](../../configs/examples/v1/) 同一布局：加载器看到 `apiVersion: daq.gateway/v1` 且 `kind: Gateway` 时，读取同目录的 `devices/`、`point-templates/`、可选的 `points/` 本机覆盖、`sinks/mqtt.yaml`。设备通过 `spec.pointTemplateId` 共用一份点位模板，加载时展开成该设备启用的点位 id。Host 不读草稿。

`--config` 或 `GATEWAY_CONFIG` 仍可指向单文件 YAML（例如 [configs/examples/gateway.yaml](../../configs/examples/gateway.yaml)）或 v1 目录。这只覆盖采集来源，主路径是页面发布。

字段对应关系写在 [配置目录说明](../config/README.md)。

## 配置与发布

```
页面编辑草稿 (data/draft)
        │  POST /api/v1/config/validate
        ▼
   校验通过后 POST /api/v1/config/publish
        │  CanonicalRevision → revisions/<hash>
        ▼
   data/published
        │  同一进程 ICollectorControl.TryReloadAsync
        │  目录监视作为磁盘改动的备份
        └─ POST /api/v1/config/rollback 回到某一 hash
```

重载失败时保留上一份正在采集的会话，进程不退出。这与缺 FOCAS 库时设备 `offline`、进程不崩的行为一致。

敏感项只写环境变量名（`usernameFromEnv` / `passwordFromEnv`），明文密码不进配置文件，也不进 revision。环境变量没设置时用户名和密码留空，加载不因此失败。

## 许可证桩

Runtime 读取 YAML 并采集时不检查许可证。Studio 的 `GET /api/v1/license` 返回未强制校验，管理 API 也不会因为缺少许可证而拒绝。许可证文件不参与 revision。

## 配置接口

`Gateway.Abstractions` 里的 `IConfigStore` / `IConfigPublisher` 是草稿与发布的边界草图。M1 真正写文件的是 Api 模块的 `ConfigStore`，它还没有实现这两个接口。Collector 不注册它们，采集循环直接读已发布 YAML。

| 接口 | 职责 |
| --- | --- |
| `IConfigStore` | 读写草稿 YAML 文档；查询已发布 revision |
| `IConfigPublisher` | 校验草稿、发布为内容寻址 revision、按 hash 回滚 |

字段级约束以 JSON Schema 为准。Studio 另外有自己的文档模型，用来序列化 YAML。

## 明确不做（M1）

OPC UA、Fanuc 以外的适配器、Fleet 云端、组态大屏、长期时序库、CNC 程序写入实开。程序相关能力仍是 `IProgramService` 只读预留，`programWrite` 默认 `false`。
