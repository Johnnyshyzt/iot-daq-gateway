# Management API（M1 冻结草案）

Config Studio 通过本 API 编辑配置并查看运行态。M1 的实现在 `src/Api`，由 `src/Host` 挂到 `http://127.0.0.1:5080`，并和采集在同一个进程。发布或回滚后 Api 调用 `ICollectorControl.TryReloadAsync`。采集未启动时运行态退回模拟数据。

本文对齐现有 Host 的模型：`Observation`、`DeviceHealth`、`AdapterStatus`、`DaqTopics`，以及滚动日志。草稿读写的边界草图是 `IConfigStore` / `IConfigPublisher`。M1 写文件的是 Api 的 `ConfigStore`，它还没有实现这两个接口。字段形状以 `schemas/` 为准。

下面有些状态码仍是合同草案，和当前 Studio 不完全一致：发布校验失败返回 `400 validation_failed`，许可证桩不拦截请求。

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
| 403 | `password_change_required` | 现场账号尚未修改一次性密码 |
| 403 | `license_required` | 合同草案。M1 桩不返回这一项 |
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

登录是本地账号，不是目录服务。`POST /api/v1/auth/login` 返回 Bearer 令牌。除登录和 `GET /api/v1/auth/posture` 外，`/api` 都要带 `Authorization: Bearer <token>`。

- 开发与 `dotnet run`：`Studio:AccountMode` 缺省为 `demo`。初始口令是 `admin` / `admin`、`engineer` / `engineer`、`viewer` / `viewer`，只适合 localhost。登录页会写明这一点。
- 现场 zip：`AccountMode` 为 `field`。首次启动把一次性密码写到 `data/auth/bootstrap-password.txt`。`admin` / `admin` 不能登录。登录响应 `mustChangePassword: true` 时，除 `GET /auth/me`、`GET /auth/posture`、`POST /auth/password` 外，接口返回 `403 password_change_required`。
- `POST /api/v1/auth/password` 正文 `{ "currentPassword", "newPassword" }`。新密码至少 8 位，不能与当前密码、用户名或演示口令相同。密码以 PBKDF2 存在 `data/auth/accounts.json`，文件里没有明文。
- 不信任客户端自报的角色头。

角色矩阵：

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
  "pointTemplates": [],
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
| `DELETE` | `/api/v1/config/devices/{id}` | 删除该设备草稿及其本机覆盖 `points/{id}.yaml`。不删除点位模板 |

设备正文含 `spec.pointTemplateId`。新建发那科设备时若省略，服务端写入内置模板 `fanuc-standard`。`fanuc.fake` 与 `fanuc.focas` 都引用适配器族为 `fanuc` 的模板。

`PUT` 时路径 `{id}` 必须等于 `metadata.id`，否则 `409 conflict`。`DELETE` 对已不存在的 id 返回 `404 not_found`。

### 点位模板

一类模板给多台同类设备用。M1 只有发那科模板（`spec.adapter: fanuc`）。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/point-templates` | 草稿中的 PointTemplate 列表 |
| `GET` | `/api/v1/config/point-templates/{id}` | 一份 PointTemplate |
| `PUT` | `/api/v1/config/point-templates/{id}` | 写入 `draft/point-templates/{id}.yaml` |
| `DELETE` | `/api/v1/config/point-templates/{id}` | 删除模板。仍被设备引用时 `409 template_in_use` |

点位 Id 必须在发那科目录（`state`、`alarm`、`program`）中。已知 Id 的 `address` 由目录填写。校验失败的中文原因包括：模板不存在、适配器族与设备不一致、模板或覆盖里出现目录以外的 Id。

### 本机覆盖（可选）

大多数设备没有这份文件。它只在某一台和模板不一致时使用（关掉一个点、改倍率等）。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/config/points/{deviceId}` | 本机覆盖 PointSet。没有覆盖时点列表为空 |
| `PUT` | `/api/v1/config/points/{deviceId}` | 写入 `draft/points/{deviceId}.yaml` |

路径必须等于 `metadata.deviceId`。同名点替换模板上的启用、单位、倍率和死区；不能新增目录以外的 Id。未出现在覆盖里的模板点保持原样。页面以模板为主，M1 不把按设备编辑点表当作主流程。

### 点位目录

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/v1/catalog/points?adapter=fanuc.fake` | 发那科目录。`fanuc.focas` 返回同一份 |
| `GET` | `/api/v1/catalog/points?adapter=` 其他值 | `404 catalog_unsupported`。M1 没有别的品牌目录 |

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

运行态读的是本进程采集会话，不是配置草稿。`mode` 为 `live` 表示数据来自 Collector；采集未启动时为 `mock`。`activeRevision` 来自已发布目录的 `.revision`；单文件配置没有这个文件时为空。

### `GET /api/v1/runtime/status`

`200`

```json
{
  "name": "Plant A Gateway",
  "siteId": "plant-a",
  "state": "running",
  "mode": "live",
  "activeRevision": "…",
  "utcNow": "2026-09-25T02:00:00Z",
  "devices": [
    {
      "id": "cnc-01",
      "displayName": "Lathe 01",
      "enabled": true,
      "adapter": "fanuc.fake",
      "status": "online",
      "lastSeen": "2026-09-25T02:00:00Z",
      "message": "fake adapter"
    }
  ],
  "recentErrors": []
}
```

`status` 为 `online`、`degraded`、`offline` 或 `disabled`。`value` 在观测接口里是字符串，数值会按不变区域格式化。

### `GET /api/v1/runtime/observations?deviceId=&limit=`

`deviceId` 可省略。Studio 默认 `limit` 为 50，网关回环最大 200。返回内存短窗口，新的在前。这不是历史库。窗口记录的是采集到的点，不只是 `change_only` 放行后的 MQTT 发布。

`200`

```json
{
  "observations": [
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

MQTT 上的 JSON 另含 `gatewayId`、`site`，时间字段名为 `ts`，主题由 `DaqTopics` 生成。

### `GET /api/v1/runtime/logs/tail?lines=200`

`lines` 默认 200。网关回环最大 2000，读取 `gateway-yyyyMMdd.log` 的末尾。

`200`

```json
{
  "lines": []
}
```

日志目录可由 `GATEWAY_LOG_DIR` 覆盖，与 Host 现有文件日志一致。

### `POST /api/v1/runtime/reload`

无正文。只在网关回环上提供。重新读取 `--config` 指向的文件或目录，换上一份新的采集会话。失败时保留上一份会话，响应仍是 `200`：`{ "reloaded": false }`。Studio 在发布和回滚之后调用它；目录监视会再触发一次。
