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
3. 重启 Host。首次连接会建表并写入目录。若数据库里还没有已发布配置，且数据目录或 `GATEWAY_CONFIG` 里有 YAML，会导入一次。
4. 没有连接字符串时进程会直接失败，不会悄悄退回 SQLite。

SQLite 使用 WAL。`data/*.db` 不进 git，现场 zip 也不打包数据库文件，第一次启动自己创建。

这一版还没有对外发布。更早的本分支构建把 `config_revisions` 的主键放在内容哈希上。启动时如果发现这张表没有 `Id` 列，会删掉它并用当前模型重建，旧的修订行不会保留。草稿和已发布槽位还在。若启动仍报这张表的结构错误，停掉 Host，删除 `data/gateway.db`（以及 `-wal` / `-shm`），再启动，让空库重新导入 `data/seed` 或仍留在磁盘上的 YAML。
