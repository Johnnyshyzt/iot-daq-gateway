# 数据库 / Database

边缘 Host 的配置、目录、报警和采样以数据库为准。SQLite 是默认嵌入库，文件在数据目录 `data/gateway.db`，不需要单独安装。PostgreSQL 是可切换的替代库。

YAML 仍是导入、导出和现场包种子的格式。采集加载和 Config Studio 的草稿读写继续落在 `data/draft` 与 `data/published`，这样已有的文件校验和回滚测试保持不变。每次写入草稿或已发布目录时，同一份内容会镜像进数据库。第一次启动若数据库里还没有该槽位，会把现有 YAML 导入进去。启动时不会用数据库覆盖磁盘上的 YAML。

## 表

| 表 | 内容 |
| --- | --- |
| `brands` / `controller_models` / `catalog_items` / `brand_items` / `adapters` | 版本化数控目录 |
| `config_bundles` 及 `config_*` | 草稿、已发布快照和修订 |
| `device_groups` | 车间 / 产线分组 |
| `users` | 从 `accounts.json` 镜像的账号，登录仍以文件为准 |
| `sample_latest` | 每台设备每个点的最新值 |
| `sample_history` | 历史采样，`timestamp_unix_ms` 为 bigint |
| `alarms` | 报警事件 |
| `schema_info` | 当前 schema 版本 |

时间列用 Unix 毫秒整数，避免 `InvariantGlobalization` 下 Npgsql 对 `DateTime` 的时区问题。以后迁到 TimescaleDB 时，可以用 `to_timestamp(timestamp_unix_ms / 1000.0)` 生成 `timestamptz`，再 `create_hypertable`。

## 为什么不用两套 EF Migration

SQLite 和 PostgreSQL 各有一套 EF Core 迁移快照，模型一改两边就会分叉。当前策略是同一套实体模型，启动时 `EnsureCreated`，并用 `schema_info.version`（现在是 1）记录结构版本。下一版结构变化写成带版本号的 SQL，按提供程序执行，而不是维护两份迁移项目。

## 保留

`SampleRetentionService` 每小时删除早于保留期的 `sample_history`，以及已恢复的旧报警。默认 14 天。配置：

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
3. 重启 Host。首次连接会建表并写入目录。若数据目录里已有 YAML，空槽位会被导入。
4. 没有连接字符串时进程会直接失败，不会悄悄退回 SQLite。

SQLite 使用 WAL。`data/*.db` 不进 git，现场 zip 也不打包数据库文件，第一次启动自己创建。
