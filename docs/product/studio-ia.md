# Studio 信息架构（M1）

Config Studio 的页面在本仓库 [`src/Web`](../../src/Web/README.md)，由 `src/Host` 托管，和采集在同一个进程。本页冻结页面树和每页依赖的 Management API。以后若拆仓，页面树仍以这里为准。

角色：`admin`、`engineer`、`viewer`。viewer 只读；engineer 可改草稿、校验、发布、回滚、连接测试；admin 在 engineer 之上管理许可证与用户。M1 允许只有一个本地 `admin`。

```
Studio
├── 概览            /
├── 设备            /devices
├── 点位            /points
├── 北向 MQTT       /sinks/mqtt
├── 发布            /publish
├── 运行态          /runtime
└── 系统            /settings
```

| 页面 | 路由 | 工程师要完成的事 | API | 谁可以改 |
| --- | --- | --- | --- | --- |
| 概览 | `/` | 看网关是否在跑、当前 revision、在线设备数、最近错误 | `GET /api/v1/runtime/status`，`GET /api/v1/config` | 只读 |
| 设备 | `/devices` | 新建或编辑一台 Fanuc / Fake，做连接测试 | `GET/PUT/DELETE /api/v1/config/devices/{id}`，`POST /api/v1/devices/{id}/test` | engineer、admin |
| 点位模板 | `/points` | 编辑设备类模板（启用、单位、倍率、死区）；看哪些设备在用。CSV 导入导出仍作用在模板上 | `GET/PUT/DELETE /api/v1/config/point-templates/{id}` | engineer、admin |
| 北向 MQTT | `/sinks/mqtt` | 填写 Broker、环境变量引用、Topic 模板，并预览 JSON 字段 | `GET/PUT /api/v1/config/sinks/mqtt` | engineer、admin |
| 发布 | `/publish` | 看草稿与已发布的差异，校验，发布，或回滚到历史 revision | `POST /api/v1/config/validate`，`POST /api/v1/config/publish`，`GET /api/v1/config/revisions`，`POST /api/v1/config/rollback` | engineer、admin |
| 运行态 | `/runtime` | 看设备健康、最近观测、日志尾 | `GET /api/v1/runtime/status`，`GET /api/v1/runtime/observations`，`GET /api/v1/runtime/logs/tail` | 只读 |
| 系统 | `/settings` | 改站点名（写入 Gateway 草稿）、查看许可证、管理用户 | `GET/PUT /api/v1/config/gateway`；许可证与用户走 API 的认证说明，不进配置 hash | 站点名：engineer、admin；许可证与用户：admin |

## 页面说明

### 概览 `/`

首屏四块信息：网关健康、已发布 revision、启用设备中的在线数、最近错误（来自状态消息或日志尾的末几行）。revision 与草稿是否脏，用 `GET /api/v1/config` 的 `publishedRevision` 与 `draftRevision` 比较。

### 设备 `/devices`

列表来自 `GET /api/v1/config/devices`。新建与编辑提交完整 Device 文档到 `PUT /api/v1/config/devices/{id}`。`metadata.id` 与路径一致。适配器只有 `fanuc.fake` 与 `fanuc.focas`。发那科设备必须选择点位模板（`spec.pointTemplateId`），不在这页重填三个点。

连接测试调用 `POST /api/v1/devices/{id}/test`，针对草稿里的连接参数。Fake 恒为成功；FOCAS 做真实握手。测试不发布配置。

删除设备时同时去掉该设备的本机覆盖草稿，不删除模板。

### 点位模板 `/points`

维护设备类模板，不是按设备各写一张地址表。一类模板给多台同类设备用。选择模板后，从发那科目录添加点，改启用、单位、倍率和死区，并看到哪些设备引用它（只读）。保存粒度是整份 PointTemplate：`PUT /api/v1/config/point-templates/{id}`。

M1 只有发那科目录：`state`、`alarm`、`program`（`fanuc.fake` 与 `fanuc.focas` 共用，`GET /api/v1/catalog/points?adapter=`）。不提供通用地址栏。YAML 里的 `address`（`cnc/statinfo`、`cnc/alarm`、`cnc/program`）由目录填写，采集按点位 id。内置模板是 `fanuc-standard`（「Fanuc 标准三态」）。

某一台和模板不一致时，可用 `points/{deviceId}.yaml` 做本机覆盖（同名替换；不能新增目录外的 id）。M1 页面不把覆盖编辑当作主流程。

CSV 导入导出作用在当前模板上。导入只接受目录中的点位 Id，地址列不当成协议地址。Excel 先另存为 CSV。其他品牌还没有目录。

### 北向 MQTT `/sinks/mqtt`

编辑唯一的 MqttSink（`sinks/mqtt.yaml`）。认证只填环境变量名，不填密码。Topic 模板默认 `daq/{site}/{deviceId}/{point}`，状态主题默认 `daq/{site}/{deviceId}/$status`。

JSON 预览只在浏览器里用模板和一条样例点拼出载荷，不连 Broker。载荷字段与运行时 MQTT JSON 一致：`gatewayId`、`site`、`deviceId`、`point`、`value`、`quality`、`unit`、`ts`。v1 加载后 `gatewayId` 是 `gw-` 加上 `metadata.siteId`，主题里的 `{site}` 就是 `siteId`。

### 发布 `/publish`

展示草稿相对 `published/` 的差异、`POST /api/v1/config/validate` 的 issues、发布按钮和最近 N 个 revision。发布成功后草稿与已发布内容对齐。回滚会把已发布指针拨回所选 hash，并把草稿重置为那一版，避免界面留下过期草稿。

### 运行态 `/runtime`

观测是内存中的短窗口，不是时序库。采集在同一进程里跑时，页面读它的状态、观测和 `logs/gateway-yyyyMMdd.log`，`mode` 为 `live`。采集关闭时页面用模拟数据，`mode` 为 `mock`。设备状态取值与 `AdapterStatus` 一致：`online`、`degraded`、`offline`；禁用设备为 `disabled`。

### 系统 `/settings`

站点显示名和 `siteId` 属于 Gateway 草稿，改完要走发布才影响运行中的主题。许可证与本地账号放在配置工作区旁，不进入 revision。角色仍是本机的 admin / engineer / viewer。演示模式可以保留默认口令；现场包首次登录必须在 `/account/password` 修改密码。

## M1 不出现的页面

OPC UA 端点、其他品牌设备向导、Fleet 地图、组态大屏、历史曲线、程序下发。这些不在信息架构里留空入口。
