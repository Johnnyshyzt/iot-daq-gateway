# Linux 安装

Windows 现场包见 [windows-install.md](windows-install.md)。这里是同一套 Host 在 Linux 上的两条路：systemd 服务，以及 Docker Compose 演示。两条都不是把 Linux 当成发那科 FOCAS 的生产路径。FOCAS 生产采集仍是 Windows x64 加现场 `Fwlib64.dll`。

## systemd

需要已安装的 ASP.NET Core 10 运行时（`dotnet` 能执行 `Host.dll`）。在构建机：

```bash
cd src/Web && npm ci && npm run build
dotnet publish src/Host/Host.csproj -c Release -o /tmp/iot-daq-gateway
```

把输出放到采集机的 `/opt/iot-daq-gateway`（里面有 `Host.dll`、`wwwroot`、`data/seed`）。然后：

```bash
sudo ./packaging/linux/install-systemd.sh
```

脚本会建立系统用户 `iot-daq`、数据目录 `/var/lib/iot-daq-gateway`，并把 `packaging/linux/iot-daq-gateway.service` 装到 systemd。服务监听 `http://127.0.0.1:5080`，账号模式是 `field`：第一次启动在数据目录的 `auth/bootstrap-password.txt` 写一次性口令，登录后必须修改。

自包含发布时，把单元文件里的 `ExecStart` 改成 `/opt/iot-daq-gateway/Host`，不再依赖机器上的 `dotnet`。

常用命令：

```bash
systemctl status iot-daq-gateway
journalctl -u iot-daq-gateway -f
sudo systemctl restart iot-daq-gateway
```

单元文件是 `Restart=on-failure`、`RestartSec=5`、`StartLimitIntervalSec=0`。进程异常退出会拉起；`systemctl stop` 不会。设备级停顿由进程内看门狗处理，见 [reliability.md](reliability.md)。

健康检查不需要登录：

```bash
curl -s http://127.0.0.1:5080/healthz
```

返回版本、数据库种类、schema 版本、运行秒数、`mqttConnected`、`mqttSpoolDepth`、`mqttSpoolDropped`，没有路径、账号或报文。升级时保留 `/var/lib/iot-daq-gateway`（`gateway.db`、`auth`，以及还没发完的 `mqtt-spool`）。停服务后再拷贝数据库，见 [ops-field.md](ops-field.md)。

## Docker Compose（Fake 演示）

```bash
docker compose -f docker/docker-compose.yml up --build
```

浏览器打开 `http://127.0.0.1:5080`。端口只绑在宿主机的回环地址上。容器内监听 `0.0.0.0:5080`，否则 `appsettings.json` 里的 `127.0.0.1` 从端口映射进不去。

命名卷 `gateway-data` 挂在 `/app/data`。第一次创建卷时，Docker 会带上镜像里的 `data/seed`。之后数据库留在卷里，改示例 YAML 不会改已经发布的配置。空目录 bind mount 盖住 `/app/data` 时看不到镜像里的种子，Host 会写入内置默认配置。

这个 Compose 是 Fake 演示，Broker 主机名是 `mosquitto`。不要把它当成生产 FOCAS 路径，也不要把 5080 发布到 `0.0.0.0`。
