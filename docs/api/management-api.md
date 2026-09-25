# Management API（M1 冻结草案）

商业 Config Studio 通过本 API 编辑配置并查看运行态。合同放在开源仓库里，使 Edge Runtime 与 Studio 可以分仓实现。本文对齐现有 Host 的模型：`Observation`、`DeviceHealth`、`AdapterStatus`、`DaqTopics`，以及滚动日志。

**实现状态：** `Gateway.Host` 当前是采集进程，还没有 HTTP 服务端。下面的路径、JSON 和状态码是 M1 冻结合同。草稿读写对应 `IConfigStore`，发布与回滚对应 `IConfigPublisher`。字段形状以 `schemas/` 为准。

## 部署

- 基路径：`/api/v1`
- 同进程嵌入时与静态前端同源
- 只监听本机回环地址。机床网与办公网不能直接访问管理端口
- 请求与响应体：`application/json; charset=utf-8`
- 资源 JSON 使用与 YAML 相同的 camelCase 字段（`apiVersion`、`kind`、`metadata`、`spec`）
- 服务端把 JSON 写成 YAML。revision 对解析后的文档哈希，因此 JSON 与 YAML 的排版不影响 hash

Studio 页面与路径的对应关系见 [studio-ia.md](../product/studio-ia.md)。目录与哈希见 [配置说明](../config/README.md)。

## 错误

失败时正文统一为：

```json
{
  "code": "not_found",
  "message": "Device cnc-99 was not found.",
  "details": { "id": "cnc-99" }
}
```

`details` 可省略。

| HTTP | `code` | 何时 |
| --- | --- | --- |
| 400 | `invalid_json` | 正文不是 JSON，或路径参数为空 |
| 401 | `unauthorized` | 配置了令牌但未携带或令牌不对 |
| 403 | `forbidden` | 角色不够 |
| 403 | `license_required` | 商业 API 找不到许可证桩 |
| 404 | `not_found` | 设备、点表或资源不存在 |
| 404 | `revision_not_found` | 回滚目标不在 `revisions/` |
| 409 | `conflict` | 路径中的 id 与 `metadata.id` / `metadata.deviceId` 不一致 |
| 422 | `validation_failed` | 发布时草稿未通过校验。`details.issues` 为问题列表 |
| 500 | `internal` | 未预期的读写失败 |

校验问题的形状：

```json
{
  "severity": "error",
  "path": "devices/cnc-01.yaml#/spec/adapter",
  "code": "schema",
  "message": "adapter must be fanuc.fake or fanuc.focas"
}
```

`path` 是 bundle 内相对路径，可带 JSON Pointer。

## 认证与角色

M1 是本地单用户或 Basic / Bearer 桩，不是完整身份系统。

- 未设置 `IOT_DAQ_STUDIO_TOKEN` 时，桩把本机同源调用视为 `admin`（仅用于同机嵌入开发）
- 设置了该环境变量时，请求必须带 `Authorization: Bearer <token>`，该令牌映射为 `admin`
- 不信任客户端自报的角色头

角色矩阵（用户目录落地后按此执行；M1 桩可以只有 admin）：

| 操作 | viewer | engineer | admin |
| --- | --- | --- | --- |
| 读取配置、运行态、日志 | 是 | 是 | 是 |
| 写草稿、校验、发布、回滚、设备测试 | 否 | 是 | 是 |
| 许可证与用户 | 否 | 否 | 是 |

viewer 调用写接口返回 `403 forbidden`。

## 许可证桩

Runtime 读取 YAML 并采集时不看许可证。Management API 需要：

- 环境变量 `IOT_DAQ_LICENSE` 指向的文件，或工作区根的 `license.json`
- 桩实现：文件可解析，且 `id` 非空即可，例如 `{ "id": "dev-stub", "licensee": "local", "expires": "2099-01-01" }`

文件不参与 revision。缺失时所有 `/api/v1` 路由返回 `403 license_required`。

## 配置资源

`GET /api/v1/config` 返回草稿整包，并带上已发布 hash，供概览和发布页判断是否脏。

`200`

```json
{
  "publishedRevision": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "draftRevision": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "gateway": {},
  "devices": [],
  "pointSets": [],
  "mqtt": {}
}
```

从未发布时 `publishedRevision` 为 `null`。某一类草稿缺失时，`gateway` / `mqtt` 为 `null`，数组为 `[]`。两个 hash 相同表示草稿与已发布内容一致。

`GET` 单资源返回文档本身（与 schema 一致），不包一层信封。`PUT` 替换整份资源，M1 没有 `PATCH`。写成功返回 `200` 和写入后的文档。这些写只改 `draft/`，运行中的采集要等 publish。

### Gateway

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/gateway` | 草稿中的 Gateway |
| `PUT` | `/api/v1/config/gateway` | 替换草稿。正文 `kind` 必须是 `Gateway` |

站点名在系统页修改，仍要发布后才进入 `published/`。这两条是冻结草案里设备 / 点位 / MQTT 之外、Gateway 资源所需要的配套。

### 设备

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/devices` | `{ "items": [ Device, ... ] }` |
| `GET` | `/api/v1/config/devices/{id}` | 一份 Device |
| `PUT` | `/api/v1/config/devices/{id}` | 写入 `draft/devices/{id}.yaml` |
| `DELETE` | `/api/v1/config/devices/{id}` | 删除该设备草稿及其 `points/{id}.yaml` |

`PUT` 时路径 `{id}` 必须等于 `metadata.id`，否则 `409 conflict`。`DELETE` 对已不存在的 id 返回 `404 not_found`。

### 点位

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/points/{deviceId}` | PointSet |
| `PUT` | `/api/v1/config/points/{deviceId}` | 写入 `draft/points/{deviceId}.yaml` |

路径必须等于 `metadata.deviceId`。设备可以尚未发布；点表可以先于发布单独保存。发布校验会要求 `deviceId` 能对上设备。

### MQTT

M1 只有一个北向 MQTT。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/sinks/mqtt` | MqttSink |
| `PUT` | `/api/v1/config/sinks/mqtt` | 写入 `draft/sinks/mqtt.yaml` |

Broker 密码只允许 `passwordFromEnv` / `usernameFromEnv`。schema 拒绝未知字段，因此带明文 `password` 的 `PUT` 返回 `422 validation_failed`。`POST /config/validate` 用 200 和 `valid: false` 报告同一问题；`POST /config/publish` 在失败时返回 422。单份资源的 `PUT` 只检查该文档的 schema 与路径身份，跨文件规则留给 validate / publish，这样草稿可以分步保存。

`tls` 对应今天单文件配置的 `mqtt.tls`，默认 `false`。QoS 为 `0`、`1` 或 `2`，与 `MqttObservationSink` 的映射一致。

## 校验、发布、回滚

### `POST /api/v1/config/validate`

正文可省略。始终校验当前草稿（跨文件规则见配置说明）。

`200`

```json
{
  "valid": false,
  "draftRevision": "…",
  "issues": []
}
```

`valid` 为 false 时 HTTP 仍是 200，便于发布页展示 issues。此路由不因为校验失败而返回 422。

### `POST /api/v1/config/publish`

无正文。先执行与 validate 相同的检查。通过后：

1. 计算 revision
2. 若 `revisions/<hash>/` 尚不存在，写入不可变快照
3. 将快照复制为 `published/`，并写 `published/.revision`
4. 把草稿重置为该快照，使界面与运行内容一致

`200`

```json
{
  "revision": "…",
  "publishedAt": "2026-09-25T02:00:00Z"
}
```

内容相同的再次发布得到同一个 revision，并仍返回 200。校验失败返回 `422 validation_failed`，`published/` 保持原样。热加载 `published/`；失败则用上一份成功的 revision 重启采集循环，并在运行态报告错误，进程不退出。

### `GET /api/v1/config/revisions?limit=20`

`limit` 默认 20，最大 100。新的在前。

`200`

```json
{
  "activeRevision": "…",
  "items": [
    { "revision": "…", "publishedAt": "2026-09-25T02:00:00Z", "active": true }
  ]
}
```

### `POST /api/v1/config/rollback`

```json
{ "revision": "…" }
```

目标必须是已存在的快照。成功后 `published/` 与草稿都变为该快照，响应与 publish 相同。回滚到当前已发布 hash 返回 200，不新写历史。未知 hash 返回 `404 revision_not_found`。

## 设备测试

`POST /api/v1/devices/{id}/test`

无正文。使用草稿中的 Device；没有草稿时用已发布副本。不写配置，也不发布。

- `fanuc.fake`：恒成功，`status` 为 `online`
- `fanuc.focas`：按 `connection.host` / `port` / `focasTimeoutMs` 做真实握手。缺 `Fwlib64.dll` 或机床不可达时 `ok` 为 false，`status` 为 `offline`，`message` 带原因。进程不退出

`200`

```json
{
  "ok": true,
  "status": "online",
  "message": "fake handshake",
  "elapsedMs": 12
}
```

`status` 与 `AdapterStatus` 的小写形式一致：`online`、`degraded`、`offline`。未知设备返回 `404 not_found`。

## 运行态

运行态读的是进程内状态，不是配置草稿。`activeRevision` 来自 `published/.revision`。在 v1 加载器接上之前，实现可以先反映今天单文件 Host 的采集状态，`activeRevision` 为空。

### `GET /api/v1/runtime/status`

`200`

```json
{
  "gateway": {
    "siteId": "plant-a",
    "name": "Plant A Gateway",
    "status": "online",
    "version": "0.3.0"
  },
  "activeRevision": "…",
  "devices": [
    {
      "deviceId": "cnc-01",
      "status": "online",
      "message": null,
      "timestamp": "2026-09-25T02:00:00Z"
    }
  ]
}
```

设备元素与 `DeviceHealth` 对应。`status` 为 `online` / `degraded` / `offline`。

网关 `status`：

- `online`：进程在服务，MQTT 已连接，且每台启用设备为 `online`
- `degraded`：进程在服务，但 MQTT 未连接，或至少一台启用设备不是 `online`

API 能应答时不用 `offline` 表示网关本身。`version` 使用程序集版本（与启动日志里的 `HostInfo.Version` 相同）。

### `GET /api/v1/runtime/observations?deviceId=&limit=`

`deviceId` 可省略。`limit` 默认 100，最大 500。返回内存短窗口，新的在前。这不是历史库。

`200`

```json
{
  "items": [
    {
      "deviceId": "cnc-01",
      "point": "state",
      "value": "RUNNING",
      "timestamp": "2026-09-25T02:00:00Z",
      "quality": "good",
      "unit": null
    }
  ]
}
```

字段对应 `Observation`：`deviceId`、`point`、`value`、`timestamp`、`quality`、`unit`。MQTT 上的 JSON 另含 `gatewayId`、`site`，时间字段名为 `ts`，主题由 `DaqTopics` 生成。

### `GET /api/v1/runtime/logs/tail?lines=200`

`lines` 默认 200，最大 2000。读取当前滚动日志文件的末尾。

`200`

```json
{
  "path": "logs/gateway-20260925.log",
  "lines": []
}
```

日志目录可由 `GATEWAY_LOG_DIR` 覆盖，与 Host 现有文件日志一致。
