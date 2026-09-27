# 北向集成 / Northbound integration

0.7.0 给 MES、SCADA 和云侧一条稳定的数据合同，以及三条出口：MQTT（原有）、HTTP 推送、只读查询 API，外加可选的 OPC UA 服务器。载荷标识是 `northbound/1.0`。Schema 在 [contract/](contract/)，Host 也提供：

- `GET /api/contract/v1`
- `GET /api/contract/v1/{name}.schema.json`（无需登录）
- `GET /api/query/v1/openapi.json`（无需登录；数据接口要 API 密钥）

`gatewayId` 一律是 `gw-` 加上已发布配置里的站点 Id。站点空着时用 `default`。时间是 ISO-8601，UTC 偏移写成 `+00:00`。

This release keeps the existing MQTT point and status JSON, and adds a versioned contract plus HTTP push, a read-only query API, and an optional OPC UA server. The schema id is `northbound/1.0`. Files are under [contract/](contract/) and are also served by the Host at the paths above. `gatewayId` is `gw-` plus the published site id (`default` when the site is blank). Timestamps are ISO-8601.

## 合同种类 / Document kinds

| kind | 何时出现 | 必填字段 |
| --- | --- | --- |
| `pointValue` | 点位采样 | `gatewayId` `site` `deviceId` `point` `quality` `ts`；`value` 与 `unit` 可空 |
| `deviceStatus` | 设备链路状态 | 同上，外加 `status`；`message` 可空 |
| `alarm` | 报警发生或恢复 | `alarmId` `action`（`raise` / `clear`）`code` `message` `severity` `point` |
| `partCount` | 产量点 | `count` `quality`；`total` 可省略 |
| `utilization` | 稼动汇总 | `from` `to` `devices[]`（运行/空闲/报警/离线毫秒、稼动比、产量）。授权包含 OEE 时同一对象多 `availability`、`performance`、`quality`、`oee`、`oeeFlag`，缺省时省略，旧字段不变 |
| `ruleEvent` | 规则事件 | MQTT `$event`。`ruleId`、`name`、`message`。HTTP 推送不单独开关这类事件 |
| `batch` | HTTP 推送 | `sentAt` `events[]`，元素是上面几种文档 |

`quality` 沿用采集侧的 `good` / `bad` / `uncertain`。报警、产量、稼动和 HTTP 批次始终带 `schema` 与 `kind`。点位和状态在 MQTT 上分两种外形，见下一节。

Examples: [point-value.v1.json](contract/examples/point-value.v1.json), [point-value.computed.json](contract/examples/point-value.computed.json), [alarm.json](contract/examples/alarm.json), [part-count.json](contract/examples/part-count.json), [utilization.json](contract/examples/utilization.json), [rule-event.json](contract/examples/rule-event.json), [batch.json](contract/examples/batch.json).

## MQTT 主题 / MQTT topics

默认点位模板是 `daq/{site}/{deviceId}/{point}`，状态主题是 `daq/{site}/{deviceId}/$status`。站点 `plant-a`、设备 `cnc-01` 时：

| 主题 | 载荷 | 何时发 |
| --- | --- | --- |
| `daq/plant-a/cnc-01/state` | 点位（这里 point 是 `state`） | 扫描到该点；`change_only` 时仅值变化才发 |
| `daq/plant-a/cnc-01/$status` | 设备状态 | 每轮扫描 |
| `daq/plant-a/cnc-01/$alarm` | 报警发生或恢复 | 采集在跑时，新发生或新恢复 |
| `daq/plant-a/cnc-01/$parts` | 产量 | 点位 Id 为 `partCount` 或 `partCountTotal`，且数值变化 |
| `daq/plant-a/$utilization` | 最近 24 小时稼动 | 约每 60 秒一条 |
| `daq/plant-a/{deviceId}/$event` | 规则触发的北向事件 | 规则动作里配置了事件时 |

`$alarm`、`$parts`、`$utilization` 的前缀从点位模板推导：取模板展开后 `/{site}/` 之前的那段，再接 `/{site}/...`。模板里没有 `/{site}/` 时退回 `daq/{site}/$utilization`。这三条始终是 v1 外形，不随下面的「契约版本」开关省略 `schema` / `kind`。

Broker 不可达时，点位、状态和这三条附加主题都进 `mqtt-spool/`，重连后按序重放。缓冲不在 SQLite 页面备份里。

### 契约版本与迁移 / Contract version and migration

Studio「北向 MQTT」的契约版本默认 `legacy`，升级后已有订阅方不用改。

`legacy` 点位（与 0.6.0 相同，没有 `schema` / `kind`）：

```json
{
  "gatewayId": "gw-plant-a",
  "site": "plant-a",
  "deviceId": "cnc-01",
  "point": "state",
  "value": "RUNNING",
  "quality": "good",
  "unit": null,
  "ts": "2026-09-27T08:00:00+00:00"
}
```

状态同样只有 `gatewayId`、`site`、`deviceId`、`status`、`message`、`ts`。完整样例见 [point-value.legacy.json](contract/examples/point-value.legacy.json) 与 [device-status.legacy.json](contract/examples/device-status.legacy.json)。

把契约版本改成 `v1` 并发布后，点位和状态多两个字段，其余字段不变：

```json
{
  "schema": "northbound/1.0",
  "kind": "pointValue",
  "gatewayId": "gw-plant-a",
  "site": "plant-a",
  "deviceId": "cnc-01",
  "point": "spindleSpeed",
  "value": 1200,
  "quality": "good",
  "unit": "rpm",
  "ts": "2026-09-27T08:00:01+00:00"
}
```

状态的 `kind` 是 `deviceStatus`。已有订阅方如果忽略未知字段，可以直接切到 `v1`。严格按字段集合校验的订阅方继续用 `legacy`，需要报警、产量、稼动时再单独订 `$alarm`、`$parts`、`$utilization`。HTTP 推送和查询 API 始终输出 v1，不受这个开关影响。

The default remains `legacy`, so existing point and `$status` subscribers see the same JSON as 0.6.0. Set the MQTT sink contract version to `v1` and publish when new consumers should receive `schema` and `kind`. Alarm, part-count, and utilization topics are always v1 and are additive: they do not replace point or status topics.

## HTTP 推送 / HTTP push

Studio「北向 HTTP」维护目标。接口是 `GET/PUT /api/v1/http-push`、`DELETE /api/v1/http-push/{id}`、`POST /api/v1/http-push/{id}/test`，写入要 engineer 或 admin。变更写入审计（`httppush.save` / `httppush.delete` / `httppush.test`）。

每个目标：

| 项 | 说明 |
| --- | --- |
| URL | `http` 或 `https` |
| 方法 | `POST` 或 `PUT` |
| 附加头 | 名称和值。读取时值显示为 `***`。保存时空字符串或 `***` 保留原值 |
| 认证 | `none`、`basic`（用户名 + 密码）、`bearer`（`Authorization: Bearer <secret>`）、`hmac-sha256` |
| HMAC | 对原始 body 做 HMAC-SHA256，十六进制小写，默认头 `X-DAQ-Signature: sha256=<hex>`。头名可改 |
| 点位 | `change`（变化）或 `periodic`（按秒批量当前值），也可关闭 |
| 状态 / 报警 | 各自开关 |
| 批量 | `batchMax`（1–500，默认 50），`batchIntervalMs`（200–60000，默认 1000） |
| 超时 | `timeoutMs`，默认 8000 |
| 重试 | `maxRetries` 限制退避指数，不丢弃报文。`backoffInitialMs` 起，按 `initial << (attempt-1)` 增长，不超过 `backoffMaxMs` |

正文始终是 v1 批次，见 [batch.json](contract/examples/batch.json)。事件先写入 `data/http-spool/{目标 Id}`，再按序送出。目标不可达或非成功状态码时留在盘上，进程重启后继续送。单目标上限 10000 条、64MB、24 小时，超出丢最旧并计入 `dropped`。页面显示已送达、失败、缓冲深度、丢弃和最近错误。密钥和附加头的值不会明文返回；`clearSecret: true` 才清空密钥。

新目标的游标从创建时刻起，不补发更早的采样、状态和报警。同一毫秒里若一批已经塞满，其余事件留到下一批。测试按钮发一条不进缓冲的探测批次。

Delivery is at-least-once. Verify `X-DAQ-Signature` against the raw body when auth is `hmac-sha256`. A shared secret that round-trips as `***` is left unchanged.

## 只读查询 API / Read-only query API

密钥在 Studio「查询接口」创建。明文只在创建响应里出现一次（`daq_` 加 32 字节的 base64url）。库里只存 SHA-256。列表只显示名称、前 12 位前缀、创建人、最近使用和吊销时间。吊销立即生效。创建和吊销写入审计（`apikey.create` / `apikey.revoke`）。密钥是只读范围，不能调用 `/api/v1`。

除 OpenAPI 外，请求带其中一种：

- `X-Api-Key: daq_...`
- `Authorization: Bearer daq_...`（仅当令牌以 `daq_` 开头；Studio 登录令牌不能查这些接口）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/query/v1/devices` | 已发布设备、车间、产线、适配器、链路状态 |
| GET | `/api/query/v1/devices/{id}/values` | 当前点位，v1 `pointValue` |
| GET | `/api/query/v1/devices/{id}/history` | `point`（可逗号分隔）、`from`、`to`（Unix 毫秒或 ISO-8601）、`bucketMs`。默认最近 1 小时，最多 20000 行 |
| GET | `/api/query/v1/alarms` | `deviceId`、`active`、`from`、`to`、`limit`（默认 200）。每条是 v1 `alarm` |
| GET | `/api/query/v1/utilization` | `deviceId`、`from`、`to`。默认最近 24 小时，正文是一条 v1 `utilization`。契约仍是 `northbound/1.0`。OEE 字段只在授权允许时出现 |
| GET | `/api/query/v1/openapi.json` | 无需密钥 |

```bash
curl -sS -H "X-Api-Key: daq_..." http://127.0.0.1:5080/api/query/v1/devices
```

Studio 只监听本机时，把查询口暴露给 MES 需要另行做反向代理，并限制来源。密钥和 HTTP 密钥、OPC UA 口令哈希都在数据库里，SQLite 页面备份会带上它们。

## OPC UA 服务器 / OPC UA server

默认关闭。Studio「OPC UA」可启用，默认端口 `48400`。端点：

`opc.tcp://127.0.0.1:48400/iot-daq-gateway`

进程监听 `opc.tcp://0.0.0.0:{port}/iot-daq-gateway`。安全策略至少开一种：

- `None` / `MessageSecurityMode.None`：演示和隔离网段
- `Basic256Sha256` / `SignAndEncrypt`：现场建议

用户令牌：匿名（可关）和可选用户名。用户名密码以 PBKDF2 存在 `app_settings` 键 `opcua`，接口只返回 `hasPassword`。不提供用户证书登录。保存写入审计 `opcua.update`，服务器按新设置重启。`GET /healthz` 增加 `opcUaEnabled` 和 `opcUaListening`，不含证书或口令。

地址空间在 Objects 下：

```
采集网关
└── {车间，空则「未分组」}
    └── {产线，空则「未分线」}
        └── {设备显示名}
            ├── 状态          String
            ├── 报警中        Boolean
            └── 点位
                └── {点位 Id} 按模板类型
```

命名空间 URI 是 `urn:iot-daq-gateway:nodes`。节点 Id 形如 `机加/一线/cnc-01/points/spindleSpeed`（路径段会去掉不适合做符号名的字符）。`good` / `bad` / `uncertain` 映射到 OPC UA StatusCode。源时间戳来自采样时间。布尔用 Boolean，整数类型用 Int64，`float` / `double` 用 Double，没有小数类型的整数用 Int64，其余用 String。

证书在数据目录 `data/opcua/pki/`：

| 目录 | 用途 |
| --- | --- |
| `own` | 本机应用证书，首次启动生成 |
| `trusted` | 信任的客户端证书。把对方证书放进来后重启服务器 |
| `rejected` | 未信任的客户端会落在这里 |
| `issuer` | 发行者证书 |

`AutoAcceptUntrustedCertificates` 关闭。演示用 `None` 时可以不交换证书。`SignAndEncrypt` 时先连一次，把 `rejected/` 里的客户端证书移到 `trusted/`，再重连。Host 以固定文化运行（`InvariantGlobalization`），服务器不加载 OPC 基金会的 en-US 资源表，状态码文本不本地化。

去掉的设备文件夹会留在地址空间里，直到进程重启。服务器只反映当前值，没有历史访问（HA）。

The stack is OPC Foundation .NET Standard 1.5.378.176, the same generation as the existing OPC UA client driver. Enable the server only on a network segment that should see machine data.
