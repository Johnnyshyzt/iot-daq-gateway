# 数据库 / Database

边缘 Host 的配置、目录、报警和采样以数据库为准。SQLite 是默认嵌入库，文件在数据目录 `data/gateway.db`，不需要单独安装。PostgreSQL 是可切换的替代库。

草稿和已发布配置的运行副本只在数据库里。Studio 保存写草稿槽位，发布把同一份内容写入已发布槽位并追加 `config_revisions`。采集通过 `IRuntimeConfigSource` 读取已发布槽位，发布或回滚后进程内重载。正常操作不写 `data/draft` 或 `data/published`。

YAML / JSON 仍然是导入、导出和现场包种子的格式：

- 数据库还没有已发布槽位时，第一次启动按顺序导入 `--config` / `GATEWAY_CONFIG`、已有的 `data/published`、旧的 `data/config`、然后 `data/seed`。没有单独指定导入路径、且磁盘上已有 `data/draft/gateway.yaml` 时，草稿槽位从那份文件导入；否则草稿是已发布内容的副本。
- `GET /api/v1/config/export` 和 `POST /api/v1/config/import` 做显式导入导出。导入只写草稿。

已有槽位之后，磁盘上的 YAML 不再被读取，也不会被启动过程改写。账号文件 `accounts.json` 仍是登录来源，数据库只镜像用户表。`data/runtime/studio.log` 仍是文本日志。

## 表

| 表 | 内容 |
| --- | --- |
| `brands` / `controller_models` / `catalog_items` / `brand_items` / `adapters` | 版本化数控目录 |
| `config_bundles` 及 `config_*` | 草稿与已发布配置。`config_bundles.json` 是往返文档，其余行是同一份内容的规范化列 |
| `config_revisions` | 发布、初始导入和回滚历史。主键是行 Id，`revision` 是内容哈希，同一哈希可以有多条动作。最多保留 30 条 |
| `device_groups` | 车间 / 产线分组 |
| `users` | 从 `accounts.json` 镜像的账号，登录仍以文件为准 |
| `sample_latest` | 每台设备每个点的最新值 |
| `sample_history` | 历史采样。列 `TimestampUnixMs` 为 bigint。索引 `ix_sample_history_device_point_time`、`ix_sample_history_time`、`ix_sample_history_device_time` |
| `alarms` | 报警事件。含代码、恢复时间、持续时长、确认人和确认时间。索引 `ix_alarms_device_raised`、`ix_alarms_active_raised`、`ix_alarms_device_code` |
| `state_transitions` | 设备状态段：状态、原始值、开始、结束。索引 `ix_state_device_started`、`ix_state_ended` |
| `app_settings` | 键值。`historyRetentionDays`、`shiftCalendar`、`demoHistorySeeded`，以及上手引导 `onboarding.dismissed` / `onboarding.passwordAck` / `onboarding.connectionTested` |
| `audit_events` | 配置变更。用户、角色、动作、对象、短说明。不记录密码。最多保留最近 500 条。索引 `ix_audit_unix` |
| `notification_channels` | 通知通道。种类、打码后的 webhook、密钥列、可选环境变量名、SMTP 字段。接口不返回密钥明文 |
| `notification_rules` | 设备 / 分组 / 级别 / 代码过滤、发生与恢复、升级、安静时段、去重和每小时上限 |
| `notification_deliveries` | 投递记录。状态、次数、错误、下次重试。最多保留 2000 条。索引 `ix_delivery_created`、`ix_delivery_status` |
| `report_schedules` | 日报与班次稼动报表计划，以及上次已发送的键 |
| `link_status` | 每台设备最近一次链路状态和说明，供查询 API 与 OPC UA |
| `http_push_targets` | HTTP 推送目标。URL、认证种类、打码后不返回的密钥、批量与退避、送达 / 失败 / 丢弃计数 |
| `api_keys` | 只读查询密钥。前缀、SHA-256、创建人、吊销时间和最近使用。不明文保存。唯一索引 `ix_api_keys_hash` |
| `schema_info` | 当前 schema 版本 |

时间列用 Unix 毫秒整数，避免 `InvariantGlobalization` 下 Npgsql 对 `DateTime` 的时区问题。以后迁到 TimescaleDB 时，可以用 `to_timestamp(timestamp_unix_ms / 1000.0)` 生成 `timestamptz`，再 `create_hypertable`。

## 为什么不用两套 EF Migration

SQLite 和 PostgreSQL 各有一套 EF Core 迁移快照，模型一改两边就会分叉。当前策略是同一套实体模型，启动时 `EnsureCreated`，并用 `schema_info.version`（现在是 5）记录结构版本。已有库在启动时补 `alarms` 的新列、创建 `state_transitions`、`app_settings`、`audit_events`、四张通知表、`link_status`、`http_push_targets`、`api_keys`，并为 `config_mqtt` 补 `ContractVersion`（默认 `legacy`），再 `CREATE INDEX IF NOT EXISTS`。从版本 1、2、3 或 4 的库启动会补到 5，不要求手工迁移。列名沿用 EF 的 PascalCase，两种数据库同一套语句。不维护两份迁移项目。MQTT 待发报文在数据目录的 `mqtt-spool/`，HTTP 推送待发报文在 `http-spool/`，都不在这些表里。OPC UA 证书在 `data/opcua/pki/`，也不在表里。`app_settings` 键 `opcua` 只存口令的 PBKDF2，不存明文。

## 保留

`SampleRetentionService` 每小时删除早于保留期的 `sample_history`、已恢复的旧报警，以及已经结束的 `state_transitions`。默认 14 天。Studio「班次与历史保留」写入 `app_settings.historyRetentionDays` 后优先于下面的配置。配置：

```json
"Database": {
  "Provider": "Sqlite",
  "ConnectionString": "",
  "HistoryRetentionDays": 14
}
```

环境变量 `DATABASE_HISTORY_DAYS` 同样有效，范围 1–3650。

## 换成 PostgreSQL

1. 准备空库，并允许 Host 进程连接。
2. 设置 `Database:Provider` 为 `Postgres` 或 `PostgreSQL`，并填写 `Database:ConnectionString`。也可以用环境变量 `DATABASE_PROVIDER` 和 `DATABASE_CONNECTION`。
3. 重启 Host。首次连接会建表并写入目录。若数据库里还没有已发布配置，且数据目录或 `GATEWAY_CONFIG` 里有 YAML，会导入一次。
4. 没有连接字符串时进程会直接失败，不会悄悄退回 SQLite。

SQLite 使用 WAL。`data/*.db` 不进 git，现场 zip 也不打包数据库文件，第一次启动自己创建。

这一版还没有对外发布。更早的本分支构建把 `config_revisions` 的主键放在内容哈希上。启动时如果发现这张表没有 `Id` 列，会删掉它并用当前模型重建，旧的修订行不会保留。草稿和已发布槽位还在。若启动仍报这张表的结构错误，停掉 Host，删除 `data/gateway.db`（以及 `-wal` / `-shm`），再启动，让空库重新导入 `data/seed` 或仍留在磁盘上的 YAML。
