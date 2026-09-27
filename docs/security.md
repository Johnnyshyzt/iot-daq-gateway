# 以太网安全与网络隔离

FOCAS 面板以太网通常是明文工业协议，**不能**直接暴露到工厂办公网或互联网。

## 建议分段

1. **机床网段**：CNC 面板 / FOCAS 端口（常见 8193）只对网关开放。
2. **采集网段**：本网关进程；南向只连机床，北向只连 MQTT、已登记的 HTTP 目标和 OPC UA 客户端。
3. **企业网段**：MQTT broker、历史库、MES；禁止机床直连。

用防火墙或独立 VLAN / L3 隔离。网关作为唯一南北向桥，而不是把 CNC 接到公司 Wi-Fi。

## 网关部署

- 以普通用户运行服务或容器，不要默认 `--privileged`。
- MQTT 生产环境启用账号与 TLS（配置项 `mqtt.tls`）。密码只放在环境变量里。Windows 现场包用安装目录的 `service.env`，由 `install-service.bat` 注入服务进程的 `MQTT_USER` / `MQTT_PASSWORD`（或页面上写的同名变量）。不要把密码写进 YAML。
- 不要在仓库、zip 或镜像里写入 FOCAS 库、机床口令、broker 密码。
- 首次运行默认 HTTP，Studio 监听 `127.0.0.1:5080`。管理员可在「HTTPS」生成自签证书（SAN 为主机名和 IP）、导入 PFX 或 PEM，或关闭。可选把 HTTP 跳到 HTTPS。绑定写在 `data/https/binding.json`，证书是 `data/https/gateway.pfx`。保存后要重启进程。Docker 需要同时发布 HTTPS 端口；没有绑定文件时 Compose 仍按 `ASPNETCORE_URLS` 走 HTTP。systemd 和 Windows 服务重启后生效。
- 开发机演示账号是 `admin` / `admin`、`engineer` / `engineer`、`operator` / `operator`、`viewer` / `viewer`，登录页会标明只适合 localhost。现场包改为一次性引导密码，首次登录必须修改。角色是本机 admin、engineer、operator、viewer。操作员和只读都不能改配置。
- 限制出站：仅允许 MQTT broker、已登记的机床地址、已配置的通知 webhook / SMTP，以及 Studio 里保存的 HTTP 推送 URL。
- 通知通道的签名密钥、HTTP 推送密钥和附加头的值存在数据库列里。接口只返回是否已设置，或把值显示成 `***`，不返回明文。页面备份的 SQLite 快照包含这些密钥，备份文件不要外传。若通知通道填写了环境变量名且该变量非空，发送时优先用环境变量。
- OPC UA 默认关闭。演示可用安全策略 None；现场用 Basic256Sha256 Sign&Encrypt，并把客户端证书放进 `data/opcua/pki/trusted/`。用户名口令以 PBKDF2 保存。不要把 48400 暴露到办公网。
- 只读查询密钥以 `daq_` 开头，库存 SHA-256。明文只在创建时返回一次。它只能访问 `/api/query/v1` 的数据接口，不能调用 Studio 的 `/api/v1`。

## 管理接口

- 除 `POST /api/v1/auth/login` 和 `GET /api/v1/auth/posture` 外，`/api/v1` 都要带 `Authorization: Bearer`。角色是本机的 admin、engineer、operator、viewer。operator 与 viewer 不能改配置。用户、HTTPS、备份恢复、许可证导入移除和升级只有 admin。写接口在进入处理程序之前按角色拒绝。
- 新密码至少 8 位，同时包含字母和数字，不能与用户名相同，也不能是演示口令。连续失败达到 `Studio:LockoutThreshold`（默认 5）后锁定 `Studio:LockoutMinutes`（默认 15）分钟。会话 `Studio:SessionMinutes` 默认 720 分钟，空闲 `Studio:IdleMinutes` 默认 120 分钟（0 表示不看空闲）。登录成功、失败、锁定和拒绝访问写入审计。
- 时钟回拨或授权状态被改时，采集继续，配置修改返回 `security_block`。自检、报文和诊断包仍可用。诊断包不含私钥、证书、账号文件和 `state.key`。配置导出里的口令打成 `***`。
- `GET /api/contract/v1` 和 `GET /api/query/v1/openapi.json` 不需要登录，只返回 schema 与接口说明，没有采样。其余 `/api/query/v1` 要 `X-Api-Key` 或 `Authorization: Bearer daq_...`。
- `GET /healthz` 不需要登录，只返回 `status`、版本、数据库种类、schema 版本、运行秒数、MQTT 是否连通、缓冲深度和已丢弃条数，以及 OPC UA 是否启用、是否在听。不返回路径、账号、报文或采样。
- 数据库备份和恢复只有 admin。页面备份是 SQLite 快照，不含 `data/auth` 里的登录口令，也不含 `mqtt-spool/`、`http-spool/` 或 `data/opcua/pki/`。它包含通知通道密钥、HTTP 推送密钥和 API 密钥哈希。PostgreSQL 不在页面里备份。
- 配置变更写入 `audit_events`（发布、回滚、设备、模板、MQTT、密码已修改、备份、恢复、通知通道和规则、报表计划、可靠性设置、HTTP 推送、查询密钥、OPC UA）。说明里不放密码、签名密钥或 API 密钥明文。

## 变更发布

`change_only` 减少噪声，但 `$status` 每轮仍会发布，便于发现离线。不要把状态主题当作鉴权手段。
