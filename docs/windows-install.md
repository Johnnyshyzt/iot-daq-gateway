# Windows 内网安装（生产路径）

第一期生产路径是 **Windows x64 采集机上的一个 Host 进程**：自包含 zip + 现场自备 `Fwlib64.dll` + `data/published` + Windows 服务开机自启。浏览器打开本机 `http://127.0.0.1:5080` 改配置并发布。

**不要把 Linux Docker 当作生产 FOCAS 路径。** Compose 镜像只用于 Fake 演示 / 附属 Mosquitto。

首次安装以本文为准。升级、备份、日志和磁盘见 [ops-field.md](ops-field.md)。症状对照见 [field-fault-guide.md](field-fault-guide.md)。尚无自有发那科机床时的验收口径见 [product/pilot-acceptance.md](product/pilot-acceptance.md)。

现场 **不需要** 安装 .NET SDK（包括钉死的 10.0.203），也不需要 git 检出本仓库。构建机才需要 SDK。

## 发布形态：自包含 win-x64

选择 **self-contained（自包含）** 而不是 framework-dependent：

| | 自包含（本仓库采用） | 依赖框架 |
| --- | --- | --- |
| 现场要装什么 | 无（zip 内带 .NET 10 运行时） | 仍要装 .NET **运行时** 10，不是 SDK，但内网机经常也没有 |
| 包体积 | 更大（约几十 MB） | 更小 |
| 离线内网 | 解压即可 | 还要先拿到 runtime 安装包 |

不使用单文件（`PublishSingleFile=false`），以便把 `Fwlib64.dll` 放到与 `Host.exe` 同一目录，供 P/Invoke 加载。

构建（开发机 / CI，需要 SDK **10.0.203**）：

```bash
# Linux 交叉编译或 Windows 本机均可
./scripts/pack-win-x64.sh
# Windows:
#   powershell -File scripts/pack-win-x64.ps1
```

产物：`artifacts/win-x64/iot-daq-gateway-<version>-win-x64.zip`。GitHub Actions 的 `pack-win-x64` 作业会上传同名 artifact。

## 现场安装

1. 把 zip 解压到固定目录，例如 `C:\iot-daq-gateway\`。必须保留全部文件，不要只拷 exe。
2. 将授权的 **64 位** `Fwlib64.dll` 放到与 `Host.exe` 同一目录。仓库和镜像从不附带该文件。缺库时进程仍运行，`fanuc.focas` 设备 `$status=offline`。
3. 包内已经有 Studio 静态页（`wwwroot`）、`data\seed` 和 `Host.exe`。第一次启动会把 `data\seed` 复制到 `data\published` 和 `data\draft`。服务的内容根目录是安装目录，不依赖系统目录。
4. **MQTT 密码**：复制 `service.env.example` 为 `service.env`，填写 `MQTT_USER` 和 `MQTT_PASSWORD`。YAML 和页面只保存这两个**变量名**（`usernameFromEnv` / `passwordFromEnv`），不要写明文密码。见下一节。
5. **建议先前台验证**：双击 `run-console.bat`。它会加载 `service.env`，再启动 `Host.exe`。看 `logs\gateway-yyyyMMdd.log` 是否打印版本号。浏览器打开 `http://127.0.0.1:5080`。
6. **首次登录**（现场包，不是开发机的演示口令）：
   - 打开 `data\auth\bootstrap-password.txt`，用里面的一次性密码登录。
   - 页面会要求马上修改密码。改完之前不能改设备或发布。
   - `admin` / `admin`、`engineer` / `engineer`、`viewer` / `viewer` **不能**作为现场长期口令。三个角色仍是本机账号。
   - 三个账号都改完后，引导文件会被删除。
7. 在页面里把设备改成 `fanuc.focas`，填写机床 IP（常见端口 **8193**）和唯一的 MQTT `clientId`，然后发布。采集读的是 `data\published`。连接测试走与采集相同的 FOCAS 握手；缺 `Fwlib64.dll`、位数不对或连不上时页面给出中文原因，**不会**因为 TCP 端口通了就显示成功。
8. 确认后 **以管理员身份** 运行 `install-service.bat`：
   - 注册服务名 `IotDaqGateway`（显示名 IoT DAQ Gateway）
   - 把 `service.env` 写进服务的环境（`MQTT_USER`、`MQTT_PASSWORD`，以及 `STUDIO_ACCOUNT_MODE=field`）
   - `start= auto`，开机自启；进程工作目录按安装目录解析 `wwwroot` 和 `data\seed`
   - 失败后自动重启
9. 本机可先用 `fanuc.fake` 看 `$status=online` 和大约 60 秒相位。真机 `$status=online` 与随机床变化的 `state` 留到客户现场，见 [product/pilot-acceptance.md](product/pilot-acceptance.md)。仓库不代做这一步。

## MQTT 密钥（只走环境变量）

页面和 `data\published\sinks\mqtt.yaml` 只保存：

```yaml
usernameFromEnv: MQTT_USER
passwordFromEnv: MQTT_PASSWORD
```

变量名可以改成别的，但必须和 `service.env` 里的名字一致。采集进程用 `Environment.GetEnvironmentVariable` 读取，明文密码不进 YAML、不进 revision、不进 zip。

`install-service.bat` 在 `sc create` 之后调用 `service-env.ps1`，把 `service.env` 写成服务注册表项：

`HKLM\SYSTEM\CurrentControlSet\Services\IotDaqGateway\Environment`（`REG_MULTI_SZ`，`NAME=VALUE`）

服务进程因此在启动时就带上这些变量。脚本只在控制台打印变量**名**，不打印值。空行和 `#` 注释会被忽略。值为空的行不注入。

修改密码后：编辑 `service.env`，再以管理员运行一次 `install-service.bat`（会重建服务并重新注入），然后确认服务已启动。只改文件、不重跑脚本，正在运行的进程看不到新值。

`run-console.bat` 用同一份 `service.env` 给前台进程，方便在注册服务前先看 MQTT 是否连上。

本仓库已经使用的 MQTT 变量名就是 `MQTT_USER` 和 `MQTT_PASSWORD`。其他环境变量（`HOST_DATA`、`STUDIO_DATA`、`GATEWAY_CONFIG`、`GATEWAY_LOG_DIR`）不是密码，安装脚本不会替你填写。

## 账号

| | 开发 / `dotnet run`（本机演示） | 现场 zip |
| --- | --- | --- |
| 模式 | `demo`（未设置 `Studio:AccountMode`） | `field`（包内 `appsettings.json`，服务还会设置 `STUDIO_ACCOUNT_MODE=field`） |
| 初始口令 | `admin` / `admin`，`engineer` / `engineer`，`viewer` / `viewer` | `data\auth\bootstrap-password.txt` 里的一次性密码 |
| 页面 | 登录页写明只适合 localhost | 登录后必须改密，否则管理接口返回 `password_change_required` |
| 存储 | `data\auth\accounts.json`，PBKDF2 哈希，无明文 | 同左。引导文件在全部账号改密后删除 |

忘记现场密码：停止服务，删除安装目录下的 `data\auth`，再启动。会重新生成引导文件。已发布的 `data\published` 不会因此被删掉。不要把演示口令留在客户机器上。

手动等价命令（管理员 cmd）只注册服务，**不会**写入 `service.env`。现场请用 `install-service.bat`，否则 `MQTT_USER` / `MQTT_PASSWORD` 进不了服务进程：

```bat
sc create IotDaqGateway binPath= "\"C:\iot-daq-gateway\Host.exe\"" start= auto DisplayName= "IoT DAQ Gateway"
sc failure IotDaqGateway reset= 86400 actions= restart/5000/restart/10000/restart/30000
powershell -NoProfile -ExecutionPolicy Bypass -File C:\iot-daq-gateway\service-env.ps1 -Mode service -ServiceName IotDaqGateway
sc start IotDaqGateway
sc query IotDaqGateway
```

## 改配置（不必重建）

在页面上发布会让同一进程重新加载 `data\published`，不必为此重启服务。直接改磁盘上的 YAML 时，目录监视也会重载；仍可以重启服务：

```bat
sc stop IotDaqGateway
sc start IotDaqGateway
```

或「服务」管理器里重启 **IoT DAQ Gateway**。

默认采集路径是程序目录下的 `data\published`，不依赖服务的工作目录。`GATEWAY_CONFIG` 或 `--config` 可以改成别的文件，那不是安装脚本的默认值。包里的 `gateway.focas.yaml` 只是参考，服务不会自动读取它。

## 卸载

以管理员运行 `uninstall-service.bat`，或：

```bat
sc stop IotDaqGateway
sc delete IotDaqGateway
```

不会删除 `data\`、`service.env`、`logs\`、`Fwlib64.dll`。

## 日志与版本

| 项 | 位置 |
| --- | --- |
| 文件日志 | 安装目录 `logs\gateway-yyyyMMdd.log`（本地时间，含日期；约 14 天） |
| 覆盖目录 | 环境变量 `GATEWAY_LOG_DIR` |
| 控制台 | 前台运行时，时间戳带日期 |
| Windows 服务 | SCM 停启；失败可看系统事件日志 |
| 版本 | 启动第一行 `iot-daq-gateway {Version}`（含程序集版本，CI 包带 git 短 SHA） |

把当天 log 拷走即可给远程排障。日志里还应有：YAML 路径、MQTT host/port/clientId、`Fwlib64.dll` 是否加载。

## 网络

| 方向 | 端口 | 用途 |
| --- | --- | --- |
| 网关 → CNC | TCP 8193（可配置） | FOCAS，仅机床网 |
| 网关 → MQTT | TCP 1883 或 8883 | 北向 |
| 浏览器 → 网关 | TCP 5080，仅 `127.0.0.1` | Studio 页面。不要改成对办公网监听 |

不要把 8193 暴露到办公网。Windows 防火墙只需放行上述出站。机床侧检查单见 [focas.md](focas.md)。

## 升级

停服务、覆盖程序、保留 `data\published`、`data\auth` 和 `service.env` 的步骤见 [ops-field.md](ops-field.md)。不要先删掉安装目录再解压。根目录的 `gateway.yaml` 不是服务读取的配置。摘要：

1. `sc stop IotDaqGateway`
2. 备份 `data\`（含 `published`、`draft`、`auth`）、`service.env`、`Fwlib64.dll` 和需要保留的 `logs\`
3. 用新 zip 覆盖二进制和 `wwwroot`。不要覆盖 `data\`、`service.env`、`Fwlib64.dll`
4. 确认 `Fwlib64.dll` 仍在 exe 旁，`service.env` 仍在
5. `sc start IotDaqGateway`，核对启动日志中的版本号。已改过的本地密码会保留；不要删 `data\auth`，除非就是要重置口令

## 常见故障

对照表在 [field-fault-guide.md](field-fault-guide.md)。页面发布会重载 `data\published`，改配置不必重启服务。换程序、换 `Fwlib64.dll`、改 `service.env` 后要重新注入并重启。

| 现象 | 处理 |
| --- | --- |
| 服务立即退出 | 看 `logs\`；常见是找不到 `data\published` 或 `data\seed` |
| 登录页拒绝 `admin` / `admin` | 现场包是正常现象。用 `data\auth\bootstrap-password.txt`，登录后改密 |
| MQTT 已认证但连不上 | `service.env` 未注入。重新以管理员运行 `install-service.bat`，并确认 YAML 里的变量名是 `MQTT_USER` / `MQTT_PASSWORD` |
| `$status=offline` 且日志含 `Fwlib64` | DLL 未放、位数不是 x64、或缺 VC 运行库（按 FANUC 说明补齐）。设备测试会给出同样的中文原因，而不是 TCP 成功 |
| `EW_SOCKET` / 连不上 | 采集机到机床 8193 不通；选件未开 |
| MQTT 反复重连 | broker 地址/端口错；发布被丢弃，采集仍继续 |
| 两台网关互踢 | `mqtt.clientId` 重复 |
| 改了页面但采集没变化 | 还没在「发布」页发布。发布后同一进程会重载 `data\published` |
