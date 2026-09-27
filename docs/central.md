# 中心管理

中心和边缘是同一个 Host。中心用 `--central` 启动，关闭采集，使用自己的数据目录。边缘保持原来的启动方式，并配置：

```json
"Central": {
  "Url": "https://central.example:5080",
  "EnrollmentToken": "一次性或限次令牌",
  "GatewayId": "edge-1",
  "Name": "一车间边缘"
}
```

`HOST_MODE=central` 与 `Host:Mode=central` 等效。`Central:BootstrapToken` 在中心第一次启动时写入一枚最多使用 20 次的注册令牌，方便编排。令牌明文只出现在创建响应里，库里是 SHA-256。

## 连接方向

边缘发起 HTTPS。注册 `POST /api/central/v1/enroll`，之后心跳 `POST /api/central/v1/heartbeat`，请求头 `Authorization: Bearer <会话>`。会话形如 `v1.{gatewayId}.{到期}.{随机数}.{HMAC}`，默认 30 天。网关 Id 不能包含点号。

这样边缘可以在 NAT 后面，不需要中心连进来。产品不签发客户端证书。需要双向 TLS 时，由现场的反向代理终结，网关之间仍用签名令牌。

中心不可达时，边缘记下失败并继续采集、报警和本地配置。下一次心跳成功后再领取待下发的模板和升级包。

## 谁管理哪一项

| 字段 | 归属 |
| --- | --- |
| 模板里的规则，且规则 Id 以 `central-` 开头 | 中心 |
| 模板声明的 `managed` 列表 | 中心 |
| `connection.host`、`connection.port`、`connection.password`、`displayName` | 现场 |
| 用户、许可证、HTTPS | 现场 |

`central-wins` 覆盖中心管理的内容，并清除脏标记。`local-wins` 在边缘调用 `POST /api/v1/agent/dirty` 或本地改过中心规则之后跳过。点位模板和通知设置本版本只保存为文档，不合并进正在采集的配置。

## 机队、下发和升级

机队页按车间分组，状态来自心跳：版本、许可证摘要、设备数、在线/离线链路、工作集。离线超过 `Central:OfflineAfterSeconds`（默认 90 秒）产生告警。

配置模板按键和版本保存。下发记录差异，回滚是再下发上一版，而不是改历史。升级包沿用页面升级的签名 zip。边缘下载后只暂存，替换程序仍走原来的重启和 `apply-upgrade`。

中心需要许可证功能 `central`。边缘不因为连上中心而继承中心的许可证。
