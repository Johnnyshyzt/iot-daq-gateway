# 现场可靠性 / Field reliability

0.6.0 把断线、Broker 不可达和采集卡住从「下一轮再试、报文丢掉」改成可观察、可恢复的行为。设置在 Studio「系统」页，也在 `GET/PUT /api/v1/ops/reliability`。写入 `app_settings` 键 `reliability`，立刻作用到正在跑的采集，不必重启。环境变量和配置里的 `Reliability:*`、`Mqtt:StoreForward:*` 只在启动时覆盖一次；之后以页面保存的值为准。

## 设备重连

每台设备有连接阶段：`connected`、`connecting`、`backoff`。失败后下一次尝试的延迟是：

`min(上限, 初始 × 倍数^(次数-1)) × (1 + (抖动样本×2 − 1) × 抖动比例)`

抖动样本在 0 到 1 之间，0.5 表示不加抖动。结果不会超过上限。默认：初始 2 秒，倍数 2，上限 120 秒，抖动 0.2。次数从 1 起算。处于 `backoff` 且还没到下次重试时间时，这一轮跳过该设备，不调用 `Collect`。

运行态 JSON 和设备健康里带 `linkPhase`、`nextRetry`、`lastError`、`attempt`。设备列表和单机详情显示阶段、下次重试和最近错误。`GET /api/v1/runtime/status` 同样带这些字段。

离线健康记为一次失败并进入退避，不重建适配器。已经连上的设备不会每轮都再 `Connect`。

## 采集看门狗

内部看门狗大约每 5 秒看一次。某台设备的采集开始时间早于停顿上限（默认 30 秒，范围 5–3600），并且阶段不是退避、期间也没有进展，就取消这次采集。只有这种停顿取消会丢掉并重建该设备的适配器，然后按退避再连。普通异常只退避，不重建。

扫描仍是一台接一台。如果某次原生调用忽略取消令牌并且一直不返回，后面的设备要等它返回。模拟器和现有异步协议驱动会尊重取消。不要把看门狗理解成能打断任意阻塞的厂商调用。

## MQTT 磁盘缓冲

Broker 连不上时，待发报文写到数据目录的 `mqtt-spool/`（`{序号}.msg.json` 加 `meta.json`）。重连后按序号重放，先于新报文；某条发送失败就停下来，顺序不变。默认上限 10000 条、24 小时、64 MB。超出条数、字节或年龄时丢掉最旧的，计数写在 `meta.json` 的 `dropped`。缓冲随进程保留，配置重载不会清空。

`GET /healthz` 增加 `mqttConnected`、`mqttSpoolDepth`、`mqttSpoolDropped`，仍然不返回路径、账号或报文。Studio「北向 MQTT」页轮询 `GET /api/v1/ops/mqtt-spool`，显示深度和已丢弃条数。没有配置缓冲目录时保持旧行为：连不上就丢掉当次发布。

页面备份是 `gateway.db` 的快照，不含 `mqtt-spool/`。停机拷贝数据目录时把这个目录一起带走，才能保住还没发出去的报文。

## 进程自愈

Windows 安装脚本在注册服务后执行：

```bat
sc failure IotDaqGateway reset= 86400 actions= restart/5000/restart/10000/restart/30000
sc failureflag IotDaqGateway 1
```

进程异常退出后，服务控制管理器按 5 秒、10 秒、30 秒重启，一天后重置计数。`failureflag` 在较老的 Windows 上可能不存在，脚本忽略失败。

Linux 单元 `packaging/linux/iot-daq-gateway.service` 是 `Restart=on-failure`、`RestartSec=5`、`StartLimitIntervalSec=0`（失败重启不设次数上限）。正常 `systemctl stop` 不会被拉起来。

## 浸泡

`tools/soak` 请求发那科模拟器的 8 个点位 Id，经 `GatewayPersistence.Write` 写入 SQLite。模拟器只发出目录里存在的点。默认 200 台、40 分钟、周期 2000 ms。它不跑完整的 LiveGateway，也不连 MQTT。实测数字在 [soak-report.md](soak-report.md)。历史表按每次采样追加，体积随时间增长是保留策略生效前的预期，不是托管堆泄漏。
