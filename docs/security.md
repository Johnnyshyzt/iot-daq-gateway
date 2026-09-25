# 以太网安全与网络隔离

FOCAS 面板以太网通常是明文工业协议，**不能**直接暴露到工厂办公网或互联网。

## 建议分段

1. **机床网段**：CNC 面板 / FOCAS 端口（常见 8193）只对网关开放。
2. **采集网段**：本网关进程；南向只连机床，北向只连 MQTT。
3. **企业网段**：MQTT broker、历史库、MES；禁止机床直连。

用防火墙或独立 VLAN / L3 隔离。网关作为唯一南北向桥，而不是把 CNC 接到公司 Wi-Fi。

## 网关部署

- 以普通用户运行服务或容器，不要默认 `--privileged`。
- MQTT 生产环境启用账号与 TLS（配置项 `mqtt.tls`）。密码只放在环境变量里。Windows 现场包用安装目录的 `service.env`，由 `install-service.bat` 注入服务进程的 `MQTT_USER` / `MQTT_PASSWORD`（或页面上写的同名变量）。不要把密码写进 YAML。
- 不要在仓库、zip 或镜像里写入 FOCAS 库、机床口令、broker 密码。
- Studio 只监听 `127.0.0.1:5080`。开发机演示账号是 `admin` / `admin` 等，登录页会标明只适合 localhost。现场包改为一次性引导密码，首次登录必须修改，角色仍是本机 admin / engineer / viewer。
- 限制出站：仅允许 MQTT broker 与已登记的机床地址。

## 变更发布

`change_only` 减少噪声，但 `$status` 每轮仍会发布，便于发现离线。不要把状态主题当作鉴权手段。
