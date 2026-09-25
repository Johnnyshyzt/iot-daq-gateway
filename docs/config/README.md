# 配置目录与 revision

配置的单一事实源是 YAML 文件。Api 通过 Management API 改草稿，校验后发布；Collector 只读已发布的那一棵树。数据库可以缓存，不能作为唯一副本。启动方式见 [README](../../README.md) 和 [open-core.md](../product/open-core.md)。

JSON Schema（draft 2020-12）在仓库 `schemas/`：

| 文件 | `kind` | 路径 |
| --- | --- | --- |
| [gateway.schema.json](../../schemas/gateway.schema.json) | `Gateway` | `gateway.yaml` |
| [device.schema.json](../../schemas/device.schema.json) | `Device` | `devices/{metadata.id}.yaml` |
| [point-template.schema.json](../../schemas/point-template.schema.json) | `PointTemplate` | `point-templates/{metadata.id}.yaml` |
| [point-set.schema.json](../../schemas/point-set.schema.json) | `PointSet` | `points/{metadata.deviceId}.yaml`（可选的本机覆盖） |
| [mqtt-sink.schema.json](../../schemas/mqtt-sink.schema.json) | `MqttSink` | `sinks/mqtt.yaml` |

`apiVersion` 固定为 `daq.gateway/v1`。一份可运行的示例包在 [configs/examples/v1/](../../configs/examples/v1/)。

## Host 读什么

主路径是数据目录里的 `published/`（v1 目录）。`--config` 或 `GATEWAY_CONFIG` 可以改成下面两种形状，那是无界面覆盖：

- **单文件**：例如 [configs/examples/gateway.yaml](../../configs/examples/gateway.yaml)。
- **v1 目录**：目录本身，或其中带 `apiVersion: daq.gateway/v1` 与 `kind: Gateway` 的 `gateway.yaml`。加载器读取同目录的 `devices/`、`point-templates/`、可选的 `points/`、`sinks/mqtt.yaml`。页面发布出的 `data/published` 就是这种目录。

Host 不读 `draft/`。`mappings/` 里如果有文件，加载直接失败。

## 包（bundle）

一个 revision 里的文件树如下。示例目录就是这样一包。

```
gateway.yaml
devices/
  cnc-01.yaml             # spec.pointTemplateId 指向模板
point-templates/
  fanuc-standard.yaml     # 一类设备一份；多台机床共用
points/                   # 可缺省。仅当某台和模板不一致时放本机覆盖
  cnc-01.yaml
sinks/
  mqtt.yaml
mappings/                 # 目录名保留。M1 没有独立映射 kind
secrets.env.example       # 只作示例，不参与 hash
.revision                 # 已发布包上的 sha256，不参与 hash
```

M1 点位主题只由 MqttSink 的 `topicTemplate` 生成。`mappings/` 里若有文件，校验失败（`unsupported`），发布被拒绝，这些文件也不进入 hash。独立映射表要等合同增加 kind 之后再启用。

发那科设备（`fanuc.fake` / `fanuc.focas`）的点位来自适配器目录，不是手填的协议地址。Studio 校验和发布只接受下面三个 id，并把 `address` 写成目录里的内部约定。采集按点位 id，不按地址栏。见 [focas.md](../focas.md)：

| 点位 id | 内部 address（目录填写） | 运行时含义 |
| --- | --- | --- |
| `state` | `cnc/statinfo` | `IDLE` / `RUNNING` / `ALARM` |
| `alarm` | `cnc/alarm` | 报警号；正常为 `0` |
| `program` | `cnc/program` | `O0001` 形式 |

点位按设备类放在 `PointTemplate` 里，不按每台设备各写一张地址表。内置模板 `fanuc-standard`（显示名「Fanuc 标准三态」）启用 `state`、`alarm`、`program`，`spec.adapter` 为 `fanuc`。`fanuc.fake` 与 `fanuc.focas` 都可以引用它。设备上的 `spec.pointTemplateId` 指向这份模板。

`points/{deviceId}.yaml` 是可选的本机覆盖，不是第二张地址表。合并规则：

- 没有该文件时，有效点位就是模板。
- 覆盖里的同名点替换模板上的启用、单位、倍率和死区。地址和数据类型仍由发那科目录填写。
- 覆盖不能新增目录以外的 id。目录里有、模板里没有的 id 可以追加（一台机床多开一个目录点）。
- 覆盖里没写到的模板点保持模板原样。要关掉某个点，必须写 `enabled: false`。

发布时校验把模板和覆盖展开成每台设备的有效点位。采集加载已发布目录时做同样的展开，把启用的点位 id 交给适配器。北向主题仍然按设备 id 发布。旧的「每台一份完整 PointSet、设备上没有 `pointTemplateId`」会在 Studio 读取时迁到默认发那科模板上：与模板相同的点表删掉，有差异的留下覆盖。没有模板、也没有点表文件时，Fake 仍只发内置的三个点。

`alarm` 在采集模型里仍是整数。目录以外的 id 不能通过 Studio 发布。

## 工作区（草稿 / 发布 / 回滚）

Studio 需要同时留下草稿和历史。工作区把多份 bundle 套在一起：

```
data/                           # HOST_DATA 或 STUDIO_DATA 可改掉
  seed/                         # 进 git，首次启动复制
  draft/                        # PUT 写这里
  published/                    # Host 默认从这里采集
    .revision                   # CanonicalRevision 的 sha256
  revisions/<sha256>/           # 不可变快照
  runtime/studio.log
```

`configs/examples/v1/` 是同一套 bundle，可 `--config configs/examples/v1` 做无界面覆盖。

`secrets.env`、`license.json`、以 `.` 开头的文件都不进入 Studio 的 revision。回滚配置不会回滚密钥或许可证。M1 许可证桩不读取 `license.json`。

## 与单文件 YAML 的字段对应

| 现用 `gateway.yaml` | v1 |
| --- | --- |
| `gateway.site` | `Gateway.metadata.siteId`（MQTT 主题里的 `{site}`） |
| `gateway.id` | v1 Gateway 没有此字段。加载器写成 `gw-` + `metadata.siteId`，MQTT 载荷的 `gatewayId` 用这个值 |
| `pipeline.sweepInterval` | `spec.acquisition.defaultIntervalMs`（毫秒） |
| `pipeline.changeOnly` | `spec.acquisition.changeOnly`。点位可只在变化时发布；`$status` 每轮仍发 |
| `programTransfer.enabled` | `spec.features.programWrite`。M1 保持 `false`，写入路径不实开 |
| `mqtt.host` / `port` / `clientId` / `qos` / `tls` | `MqttSink.spec.broker` 与 `spec.qos` |
| `mqtt.username` / `mqtt.password` | `usernameFromEnv` / `passwordFromEnv`。契约里没有明文字段 |
| `devices[].id` | `Device.metadata.id`，且等于文件名 |
| `devices[].adapter` | `spec.adapter`：`fanuc.fake` 或 `fanuc.focas` |
| `devices[].enabled` | `spec.enabled` |
| `devices[].options.host` / `port` | `spec.connection.host` / `port` |
| `devices[].options.timeoutMs` | `spec.connection.focasTimeoutMs`。FOCAS 调用按秒向上取整，与 [focas.md](../focas.md) 一致 |
| （适配器内置的 state/alarm/program） | `point-templates/{id}.yaml`，设备用 `spec.pointTemplateId` 引用；`points/{deviceId}.yaml` 只作本机覆盖 |

设备上的 `intervalMs` 覆盖网关的 `defaultIntervalMs`。采集循环只有一个周期，取已启用设备里最小的间隔，并且不低于 100 毫秒。没有启用设备时用 `defaultIntervalMs`（同样不低于 100 毫秒）。

环境变量名写在 `usernameFromEnv` / `passwordFromEnv`。Host 用 `Environment.GetEnvironmentVariable` 解析；变量未设置或为空时用户名和密码留空，不因此拒绝配置。

## Revision 算法

发布时用 `Studio.Host.Config.CanonicalRevision`，不是按文件列表拼 JSON。

1. 把草稿收成一份配置包：Gateway、按 `metadata.id` 排序的 Device、按 `metadata.id` 排序的 PointTemplate（点位再按 `id` 排序）、按 `deviceId` 排序的 PointSet 覆盖（点位再按 `id` 排序）、MqttSink。
2. 用 camelCase JSON 序列化，忽略 null。对象键按 Unicode 码点递归排序。数组保持排序后的顺序。
3. 紧凑 JSON，UTF-8，不做 `\u` 转义，末尾不加换行。
4. revision 是该字节的 SHA-256，小写十六进制。
5. `published/.revision` 的内容是 `<hex>\n`。这个文件不参与下一轮哈希。

相同内容再次发布得到同一个 revision。`configs/examples/v1/.revision` 来自更早的「文件列表」草案，数值和 CanonicalRevision 不一定相同。采集只把该文件当作当前标签读出来；从页面发布之后，文件会被写成 CanonicalRevision。

## 发布时的跨文件校验

JSON Schema 约束单个文档。`POST /api/v1/config/validate` 在此之上检查：

- 恰好一份 Gateway、恰好一份 MqttSink
- 文件名与身份一致：`devices/{metadata.id}.yaml`、`point-templates/{metadata.id}.yaml`、`points/{metadata.deviceId}.yaml`、`sinks/mqtt.yaml`
- `kind` 与路径匹配
- 发那科设备必须引用已有的点位模板，且模板适配器族为 `fanuc`
- 每个 PointSet 覆盖的 `deviceId` 能找到 Device；点位 `id` 在该文件内唯一，且必须在发那科目录中
- 每台 `enabled` 的设备展开后至少有一个启用点（省略 `enabled` 视为 `true`）
- 适配器只能是 `fanuc.fake` 或 `fanuc.focas`
- `mappings/` 下没有文件（M1 不发布独立映射）。Collector 加载时会拒绝这个目录。Api 的校验器还不会扫描它

校验失败不写入 `published/`。
