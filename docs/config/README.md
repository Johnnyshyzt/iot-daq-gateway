# 配置目录与 revision

配置的单一事实源是 YAML 文件。Studio 通过 Management API 改草稿，校验后发布；Edge Runtime 将来只读已发布的那一棵树。数据库可以缓存，不能作为唯一副本。

JSON Schema（draft 2020-12）在仓库 `schemas/`：

| 文件 | `kind` | 路径 |
| --- | --- | --- |
| [gateway.schema.json](../../schemas/gateway.schema.json) | `Gateway` | `gateway.yaml` |
| [device.schema.json](../../schemas/device.schema.json) | `Device` | `devices/{metadata.id}.yaml` |
| [point-set.schema.json](../../schemas/point-set.schema.json) | `PointSet` | `points/{metadata.deviceId}.yaml` |
| [mqtt-sink.schema.json](../../schemas/mqtt-sink.schema.json) | `MqttSink` | `sinks/mqtt.yaml` |

`apiVersion` 固定为 `daq.gateway/v1`。一份可运行的示例包在 [configs/examples/v1/](../../configs/examples/v1/)。

## 今天 Host 读什么

`Gateway.Host` 仍加载**单文件** YAML：`--config <file>` 或环境变量 `GATEWAY_CONFIG`。默认示例是 [configs/examples/gateway.yaml](../../configs/examples/gateway.yaml)。`configs/examples/v1/` 不会被当前加载器选中。Fake + MQTT 快速开始保持不变。

v1 多文件目录是 Studio 与后续加载器的合同。加载器接上之后，Runtime 读 `published/`，不再把草稿当运行配置。

## 包（bundle）

一个 revision 里的文件树如下。示例目录就是这样一包。

```
gateway.yaml
devices/
  cnc-01.yaml
points/
  cnc-01.yaml
sinks/
  mqtt.yaml
mappings/                 # 目录名保留。M1 没有独立映射 kind
secrets.env.example       # 只作示例，不参与 hash
.revision                 # 已发布包上的 sha256，不参与 hash
```

M1 点位主题只由 MqttSink 的 `topicTemplate` 生成。`mappings/` 里若有文件，校验失败（`unsupported`），发布被拒绝，这些文件也不进入 hash。独立映射表要等合同增加 kind 之后再启用。

点位地址与现有 Fake / FOCAS 桩一致，见 [focas.md](../focas.md)：

| 点位 id | address | 运行时含义 |
| --- | --- | --- |
| `state` | `cnc/statinfo` | `IDLE` / `RUNNING` / `ALARM` |
| `alarm` | `cnc/alarm` | 报警号；正常为 `0` |
| `program` | `cnc/program` | `O0001` 形式 |

示例 PointSet 按契约把这三点标成 `dataType: string`。当前 Fake 适配器在点表加载之前仍会把 `alarm` 发成数字。v1 加载器应按 PointSet 解释类型。

## 工作区（草稿 / 发布 / 回滚）

Studio 需要同时留下草稿和历史。工作区把多份 bundle 套在一起：

```
config/                         # IOT_DAQ_CONFIG_DIR 或 --config-dir
  draft/                        # Studio PUT 写这里
    gateway.yaml
    devices/
    points/
    sinks/
  published/                    # 将来 Runtime 只读这里
    ...同 bundle...
    .revision                   # 当前生效的 sha256
  revisions/
    <sha256>/                   # 不可变快照，目录名即 hash
      ...同 bundle，不含密钥...
  secrets.env                   # 真实密钥，不进 git，不进快照
  secrets.env.example
  license.json                  # 商业 API 桩，不进 hash
```

`secrets.env`、`license.json`、以 `.` 开头的文件都不进入 revision。回滚配置不会回滚密钥或许可证。

`--config-dir` 与今天的 `--config <文件>` 是两条路径。单文件参数继续服务现有 Host。

## 与单文件 YAML 的字段对应

| 现用 `gateway.yaml` | v1 |
| --- | --- |
| `gateway.site` | `Gateway.metadata.siteId`（MQTT 主题里的 `{site}`） |
| `gateway.id` | v1 Gateway 没有此字段。现有 MQTT 载荷里的 `gatewayId` 仍来自单文件配置，直到加载器落地 |
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
| （适配器内置的 state/alarm/program） | `points/{deviceId}.yaml` |

设备上的 `intervalMs` 覆盖网关的 `defaultIntervalMs`。

## Revision 算法

发布时把 bundle 打成规范 JSON，再取 SHA-256。YAML 排版、键顺序和注释不影响 hash。缺省字段不会在哈希前被填上：文件里没写的可选属性就不进入文档。

参与哈希的路径（正斜杠），按 Unicode 码点升序：

- `gateway.yaml`
- `devices/*.yaml`
- `points/*.yaml`
- `sinks/*.yaml`

排除：`secrets.env`、`secrets.env.example`、`license.json`、以 `.` 开头的文件（含 `.revision`）、说明文件。

规范形式是一个 JSON 对象：

```json
{"files":[{"document":{},"path":"devices/cnc-01.yaml"}]}
```

规则：

1. 用 YAML 解析每个文件。映射变为对象，序列变为数组，布尔与整数保持布尔与整数（`1` 不能变成 `1.0`）。
2. `files` 按 `path` 升序。每个元素的键按码点排序，因此顺序是 `document` 然后 `path`。
3. 对象键递归按 Unicode 码点排序。数组保持原顺序（点位顺序有意义）。
4. 序列化为 UTF-8、无 BOM、紧凑 JSON：分隔符只有逗号和冒号，没有多余空白。不转义 `/`。非 ASCII 以 UTF-8 原文写入，不做 `\u` 转义（本示例为 ASCII）。
5. 在 JSON 文本末尾追加一个 LF（`0x0A`）。
6. revision 是该字节序列的 SHA-256，小写十六进制，共 64 字符。
7. `published/.revision` 的内容是 `<hex>\n`。这个文件本身不参与下一轮哈希。

与 Python 对齐时，在解析结果已是 `int` / `bool` / `str` 的前提下：

```python
json.dumps(bundle, ensure_ascii=False, separators=(",", ":"), sort_keys=True) + "\n"
```

`configs/examples/v1/.revision` 就是按此算法对示例包（不含 `secrets.env.example`）算出的值。相同文档必须得到相同 hash。内容相同的再次发布复用该 revision，并把已发布指针指过去。

## 发布时的跨文件校验

JSON Schema 约束单个文档。`POST /api/v1/config/validate` 在此之上检查：

- 恰好一份 Gateway、恰好一份 MqttSink
- 文件名与身份一致：`devices/{metadata.id}.yaml`、`points/{metadata.deviceId}.yaml`、`sinks/mqtt.yaml`
- `kind` 与路径匹配
- 每个 PointSet 的 `deviceId` 能找到 Device；点位 `id` 在该文件内唯一
- 每台 `enabled` 的设备至少有一个启用点（省略 `enabled` 视为 `true`）
- 适配器只能是 `fanuc.fake` 或 `fanuc.focas`
- `mappings/` 下没有文件（M1 不发布独立映射）

校验失败不写入 `published/`。
