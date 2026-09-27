# 变更记录

## 0.7.0

给 MES / SCADA / 云侧一条稳定的北向合同，以及 HTTP 推送、只读查询和可选 OPC UA 服务器。说明见 [docs/integration.md](docs/integration.md)。

- JSON Schema 放在 `docs/contract/`，标识 `northbound/1.0`。Host 无需登录提供 `GET /api/contract/v1` 和对应 schema 文件。
- MQTT 点位和 `$status` 默认仍是 0.6.0 的字段（契约版本 `legacy`）。改成 `v1` 并发布后，这两类多 `schema` 与 `kind`。报警 `$alarm`、产量 `$parts`、稼动 `$utilization` 是新增主题，始终为 v1。Broker 不可达时仍进原来的 `mqtt-spool/`。
- Studio「北向 HTTP」可配 URL、POST/PUT、附加头和认证（无、Basic、Bearer、HMAC-SHA256）。点位按变化或周期发送，状态和报警可单独开关。失败写入 `data/http-spool/`，按条数、体积和 24 小时丢最旧，重试带退避。页面显示送达、失败、缓冲深度和丢弃。密钥与头的值不以明文返回。
- Studio「查询接口」签发只读 API 密钥。明文只显示一次，库存 SHA-256，可吊销。`GET /api/query/v1` 提供设备、当前值、历史、报警和稼动。`GET /api/query/v1/openapi.json` 无需密钥。
- Studio「OPC UA」可开关服务器，默认端口 48400，端点 `opc.tcp://127.0.0.1:48400/iot-daq-gateway`。策略为 None 与 Basic256Sha256 Sign&Encrypt，可选匿名和用户名。地址空间按车间 / 产线 / 设备展开，变量带质量和源时间戳。证书在 `data/opcua/pki/`。
- 上述配置变更写入审计。数据库结构版本为 5。已有的 1、2、3、4 在启动时补 `link_status`、`http_push_targets`、`api_keys` 和 `config_mqtt.ContractVersion`。

没有做的事：不为厂商 SDK 品牌发明调用；不把 Linux 当成 FOCAS 生产路径；不在页面里备份 PostgreSQL。OPC UA 没有用户证书登录，也没有历史访问，只提供当前值。HTTP 推送不补发目标创建之前的采样。稼动 MQTT 是约 60 秒一次的滚动 24 小时汇总，不是按班次补历史。产量主题只认点位 Id `partCount` 与 `partCountTotal`。Swagger 是 OpenAPI JSON，没有交互式浏览器。未信任的 OPC UA 客户端证书不会自动接受。MQTT 缓冲的磁盘格式没有改成 HTTP 那套队列。`InvariantGlobalization` 下 OPC 状态码文本不本地化。从地址空间去掉的设备节点要等进程重启才消失。0.6.0 里安静时段不补发、停机后只补最近一个班次报表、`mqtt-spool/` 不在 SQLite 页面备份里，这些仍然成立。`http-spool/` 同样不在页面备份里；API 密钥哈希、HTTP 密钥和 OPC UA 口令哈希在数据库里，备份文件要妥善保管。

## 0.6.0

现场可靠性，以及报警通知和稼动报表。

- 南向断线按指数退避加重试抖动，上限可配。每台设备有 `connected` / `connecting` / `backoff`，带下次重试时间和最近错误。运行态 API、设备列表和单机详情都能看到。
- MQTT 在 Broker 不可达时把报文写到数据目录 `mqtt-spool/`，按条数、体积和年龄丢弃最旧的并计数。重连后按顺序重放。深度和丢弃数出现在「北向 MQTT」和 `GET /healthz`。
- 采集看门狗发现某台设备的采集停住超过上限后，取消并重建该设备的适配器。Windows 服务注册仍设置失败重启（5 秒 / 10 秒 / 30 秒），并尝试 `sc failureflag`。systemd 单元是 `Restart=on-failure`，`StartLimitIntervalSec=0`。
- `tools/soak` 用模拟器采样加 SQLite 写入做可重复浸泡。一次实测写在 [docs/soak-report.md](docs/soak-report.md)。
- Studio「通知」可配企业微信群机器人、钉钉机器人（可选加签）、飞书 / Lark（可选签名）、SMTP 和通用 Webhook。密钥不明文返回。每条通道可发测试。
- 规则按设备、车间或产线、级别、报警代码过滤，支持发生 / 恢复、未确认升级、安静时段、去重和每小时上限。「通知记录」显示投递状态，失败可重试。日报和班次稼动摘要可定时发到所选通道，页面可预览。
- 上述配置变更写入审计。数据库结构版本为 4。已有的 1、2、3 在启动时补通知表。

说明见 [docs/reliability.md](docs/reliability.md) 与 [docs/notifications.md](docs/notifications.md)。

没有做的事：不为厂商 SDK 品牌发明调用；不把 Linux 当成 FOCAS 生产路径；不在页面里备份 PostgreSQL。安静时段内的发生和恢复直接跳过，结束后不补发。进程停机期间若有多个班次结束，启动后只补最近一个班次报表。`mqtt-spool/` 不在 SQLite 页面备份里；通知密钥在数据库里，备份文件要妥善保管。忽略取消令牌的阻塞式原生采集仍会拖住后面的设备，直到那次调用返回。浸泡测的是 200 台模拟器、每台 8 个点的采样和 SQLite 写入，不是 200 路真实协议，也没有连 MQTT Broker。没有现场机床验收。

## 0.5.0

上手引导、审计、SQLite 备份，以及把 Studio 从模板外壳收成产品界面。

- 总览在未完成时显示五步引导：确认密码、添加设备、测试连接、发布、查看实时数据。侧栏「上手引导」可以随时打开。演示口令可以稍后修改；现场一次性口令不能跳过。
- `audit_events` 记录配置变更（发布、回滚、设备、模板、MQTT、密码已修改、备份、恢复）。不记录密码。最多保留最近 500 条。Studio「审计」页可看。
- 管理员可在「系统」页下载一致的 SQLite 快照，或从该文件恢复。恢复前保留 `gateway.db.bak`。页面备份不含 `data/auth`。PostgreSQL 不在页面备份。
- `GET /healthz` 无需登录，返回状态、版本、数据库种类、schema 版本和运行秒数。
- 数据库结构版本为 3。已有的 1 或 2 在启动时补 `audit_events` 和索引。
- 总览的实时流断开后会按退避重新连接，期间仍拉取总览。
- 删除设备、删除模板、回滚配置前会确认。设备地址和采集周期在保存草稿时给出与校验器一致的提示。
- 去掉侧栏里的模板「团队」菜单、Shadcn 标题和失效头像。登录页、外观设置、错误页改为中文。产品名是「采集网关」。
- Docker Compose 把 Studio 发布到宿主机 `127.0.0.1:5080`，并用命名卷保留数据库。新增 Linux systemd 单元和 [docs/linux-install.md](docs/linux-install.md)。

没有做的事：不为厂商 SDK 品牌发明调用；不把 Linux 当成 FOCAS 生产路径；不在页面里备份 PostgreSQL；没有做长时间的 50 台以上内存浸泡，只验证了 50 台模拟器采样不会抛错。

## 0.4.0

上一份已标记的 Host 包（数据库、19 品牌目录、协议驱动、可视化）。

## 0.3.0

只有 Gateway 的历史包，没有 Studio。不要把它当成现在的 Host。
