# 可视化 / Visualization

Studio 的总览、单机、历史曲线、报警和稼动率都由 Host 计算后返回。页面不拉取原始 `sample_history`。图表用 [Recharts](https://recharts.org/)（历史曲线、报警统计、稼动率柱状图）。大屏和单机详情用页面内的色块与仪表，不另加载 ECharts。路由按页面拆包。

接口都在 `/api/v1`，除登录外要带 `Authorization: Bearer`。`GET /live/stream` 是 SSE，浏览器 `EventSource` 不能加请求头，Studio 用 `fetch` 读流。确认和保存班次需要 engineer 或 admin。

## 页面

| 路径 | 内容 |
| --- | --- |
| `/` | 总览。按车间、产线铺设备卡片，颜色对应运行 / 待机 / 报警 / 离线 / 停机。显示各状态台数、今日稼动率、今日产量、活动报警数 |
| `/board` | 同一份总览的深色全屏。侧栏隐藏，可点「全屏」或按 F11 |
| `/monitor/$deviceId` | 单机。今日状态条、主轴转速 / 负载、进给、倍率、当前程序、轴位置、件数、活动报警、最近事件、连接健康 |
| `/history` | 选择设备和数据项、时间范围。折线可缩放。导出 CSV |
| `/alarms` | 活动报警与历史，可按设备、代码筛选。确认不清除报警。按代码和设备的 Top 统计 |
| `/utilization` | 按班次、按日、按产线。日期范围。导出 CSV 和 Excel（SpreadsheetML `.xls`） |
| 系统设置 | 历史保留天数、时区、班次日历 |

总览优先走 `GET /api/v1/live/stream`（事件名 `overview`，约 1 秒合并一次变化，12 秒无变化也发心跳）。流断开时退回每 3 秒拉 `GET /api/v1/dashboard/overview`。

## 状态

采集写入的原始状态收成五种：`running` 运行、`idle` 待机、`alarm` 报警、`offline` 离线、`stopped` 停机。英文 `RUNNING` / `IDLE` / `ALARM` / `OFFLINE` / `STOPPED` 以及常见中文子串都认。质量为 `bad` 的状态点记为离线。

卡片颜色：设备被禁用为停机；否则用尚未结束的状态段；没有状态段时用最新 `state` 采样；健康状态为离线则为离线；其余为待机。

每次状态变化写入 `state_transitions`（开始、结束、原始值）。段与段之间、以及最后一段之后到查询终点，按离线计入，不计入运行。

## 报警

同一批采样里，目录报警点（点名含 `alarm`、`alarmNumber`、以 `Alarm` 或 `_warningNumber` 结尾）优先于 `state == ALARM`。一批只开一条报警，避免状态和报警号各记一次。`estop` 单独记，严重程度为 `estop`。

值为 `0`、`none`、`正常`、`OK`、`false` 不算报警。代码从报文里取第一段形如 `EX100` 的标记；只有状态报警时代码为 `STATE`，急停为 `ESTOP`。恢复时写入 `ClearedUnixMs` 和 `DurationMs`。确认只填确认人和时间，不结束报警。

## 历史曲线

`GET /api/v1/samples/series` 最多 8 台设备、8 个点。未指定 `bucketMs` 时按跨度选择桶，目标不超过约 800 个点：

| 跨度 / 800 | 桶 |
| --- | --- |
| ≤ 5 秒 | 5 秒 |
| ≤ 1 分钟 | 1 分钟 |
| ≤ 5 分钟 | 5 分钟 |
| 更长 | 按小时对齐 |

每个桶返回 `avg` / `min` / `max` / `count`。数据库侧按 `TimestampUnixMs / bucket` 分组。`samples/history` 仍保留，给旧的实时页按桶取最后一条。

## 稼动率与产量

稼动率 = 运行时间 / 计划时间。计划时间按班次与查询窗口的重叠比例缩放。默认时区 `Asia/Shanghai`（Windows 上回退 `China Standard Time`）：

| 班次 | 起止 | 计划时间 |
| --- | --- | --- |
| 白班 | 08:00–20:00 | 660 分钟 |
| 夜班 | 20:00–08:00 | 660 分钟 |

夜班跨午夜，日期取班次开始的本地日。班次不能重叠，计划分钟不能超过班次长度。`GET/PUT /api/v1/viz/settings` 保存班次 JSON 和 `historyRetentionDays`（1–3650）。

产量是班次窗口内 `partCount` 的增量。计数回绕时（当前值小于上一条）增量取当前值，不把回绕前的数减掉。窗口前的最后一条作为基线，这样班次内第一条的增量是真实差值。查询终点不包含等于终点时刻的采样。

## 演示数据

空库且已发布配置里至少有一台 `*.sim` 时，启动写入约 20 小时的状态、报警和件数，然后把 `app_settings.demoHistorySeeded` 设为 `1`。种子里除 `cnc-01`（仍是 `fanuc.fake` 三态模板）外，还有发那科、台达、广数、宝元、凯恩帝、精雕六台模拟器，分在一车间和二车间。模拟器按设备 id 分六种节拍，90 秒周期内会经过运行、待机、报警、停机或离线，并给出报警文案和件数。只有 `fanuc.fake`、没有模拟器时不灌这段历史，以免拖慢测试。

## 保留与索引

`SampleRetentionService` 每小时删除早于保留期的 `sample_history`、已恢复报警，以及已结束的 `state_transitions`。保留天数优先读 `app_settings.historyRetentionDays`，否则用 `Database:HistoryRetentionDays` / `DATABASE_HISTORY_DAYS`，默认 14。

`sample_history`、`alarms`、`state_transitions` 的索引名在 SQLite 和 PostgreSQL 上相同，列名是 EF 的 PascalCase。已有库启动时补列、补表、补索引，并把 `schema_info.version` 写成 3（含 `audit_events`）。细节见 [database.md](database.md)。
