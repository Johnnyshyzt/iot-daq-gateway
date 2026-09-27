# 报警通知与稼动报表 / Alarm notifications

0.6.0 在 Studio「通知」配置出站通道和规则，「通知记录」查看投递。结构版本是 4。启动时已有的 1、2、3 会补上 `notification_channels`、`notification_rules`、`notification_deliveries`、`report_schedules` 和投递索引。不需要手工迁移。

通知工人大约每 2 秒扫一次。第一次运行把游标 `notify.epochUnixMs`、`notify.raiseCursor`、`notify.clearCursor` 设成当前时间，演示历史里的旧报警不会补发。

## 通道

| 种类 | 请求 |
| --- | --- |
| `wecom` 企业微信群机器人 | `POST` JSON：`{"msgtype":"markdown","markdown":{"content":"…"}}`。密钥在 webhook 查询串里，不再另签 |
| `dingtalk` 钉钉自定义机器人 | 正文 `{"msgtype":"markdown","markdown":{"title","text"}}`。填写加签密钥时，HMAC-SHA256 的密钥和待签名串都是 `毫秒时间戳\n密钥`，结果 Base64 后 URL 编码，追加 `timestamp` 与 `sign` |
| `feishu` / `lark` 飞书或 Lark 机器人 | 无密钥时 `{"msg_type":"text","content":{"text"}}`。有密钥时 HMAC-SHA256 的密钥是 `秒级时间戳\n密钥`、消息为空字节，Base64 放进 JSON 的 `sign`，并带 `timestamp` |
| `smtp` | `System.Net.Mail` 发到 `mailTo`。主机、端口、账号、发件人、是否 SSL 写在通道上 |
| `webhook` | camelCase JSON：`source`、`kind`、`deviceId`、`severity`、`code`、`message`、`unixMs`。头 `X-Daq-Timestamp` 为秒。有密钥时再加 `X-Daq-Signature: sha256=<十六进制>`，HMAC-SHA256 的密钥是通道密钥、消息是正文 |

中文按原文写入 JSON，不转成 `\u` 转义。

密钥放在通道的 `Secret` 列。若填了 `SecretFromEnv` 且该环境变量非空，发送时用环境变量，与 MQTT 的变量名方式相同。`GET` 只返回 `hasSecret`，不返回明文。查询串里的 `key`、`access_token`、`secret`、`sign`、`timestamp` 在响应里变成 `***`。保存时密钥留空表示保留原值；`clearSecret` 才清空。把已经打码的 URL 原样提交会保留库里的地址。审计说明里没有密钥。

每条通道有「发送测试」。测试也记一条投递，动作是 `notify.test`。

SQLite 页面备份包含通道行，因此包含这些密钥。备份文件要和 `data/auth` 一样限制接触范围。`data/secrets` 不是单独的密钥文件。

## 规则

一条规则指向一个通道，可选：

- 设备 Id 列表。空表示不限设备。
- 车间或产线名。空表示不限分组。匹配设备的车间或产线即可。
- 严重级别列表。空表示不限。
- 报警代码。多个用逗号、分号、空格或换行分开。无 `*` 时按包含匹配；有 `*` 时按通配（不区分大小写）。留空表示全部代码。
- 发生（`onRaise`，默认开）和恢复（`onClear`，默认关）。
- 升级：未确认的活动报警超过 N 分钟后，再发到另一个通道。只对游标建立之后发生的报警。同一条报警只升级一次。安静时段内不升级，时段结束后若仍未确认才会发。
- 安静时段：本地时钟 `HH:mm` 到 `HH:mm`，可跨午夜。时段内的发生和恢复直接跳过，游标照常前进，结束后不补发。起止相同或格式不对则视为未设置。时区沿用班次日历，默认 `Asia/Shanghai`。
- 去重秒数（默认 300）和每小时上限（默认 30）。同一规则、设备、代码、种类在窗口内已有投递则不再发；该规则最近一小时的投递数达到上限也不再发。

投递状态是 `pending`、`sent`、`failed`。失败后第 1 次过 30 秒、第 2 次过 2 分钟、其后过 10 分钟再试，第 4 次仍失败则标为 `failed`。通知记录页可以对失败记录立即重试。最多保留 2000 条。HTTP 超时 8 秒，超时记为失败而不是 500。

写操作要 admin 或 engineer。动作：`notify.channel`、`notify.channel.delete`、`notify.rule`、`notify.rule.delete`、`notify.test`、`notify.retry`、`notify.report`、`reliability.update`。

## 稼动报表

「通知」页的计划：总开关、每天本地时刻（默认 08:00）发送前一个本地自然日，以及班次结束时发送该班。通道可多选。预览只生成文本，不发送。

日报在本地时刻到达后发一次，键是前一天的 `yyyy-MM-dd`。班次取最近 18 小时内已经结束、且还没发过的最近一个班。进程停机期间若有多个班结束，启动后只补最近的那一个。正文含每台设备的稼动率、件数和最多 3 个报警代码，最多 40 台，约 3500 字截断。

接口：

| 方法 | 路径 |
| --- | --- |
| GET / PUT | `/api/v1/notifications/channels` |
| DELETE | `/api/v1/notifications/channels/{id}` |
| POST | `/api/v1/notifications/channels/{id}/test` |
| GET / PUT | `/api/v1/notifications/rules` |
| DELETE | `/api/v1/notifications/rules/{id}` |
| GET | `/api/v1/notifications/deliveries?limit=` |
| POST | `/api/v1/notifications/deliveries/{id}/retry` |
| GET / PUT | `/api/v1/notifications/reports` |
| GET | `/api/v1/notifications/reports/preview?cadence=daily\|shift` |
