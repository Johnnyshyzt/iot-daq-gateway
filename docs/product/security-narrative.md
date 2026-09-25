# 安全口径

给销售和实施复述，对着当前产品说。网络分段的建议见 [security.md](../security.md)。现场口令、升级和日志位置见 [windows-install.md](../windows-install.md)、[ops-field.md](../ops-field.md)。

本产品 **没有** SOC 2、ISO 27001 或其他安全认证。通话和合同里不要补上。

## Studio 只放在采集机本机

现场包和开发用的 Host 配置都把页面绑在 `http://127.0.0.1:5080`（`Urls`）。浏览器在采集机上打开这个地址。

这是配置里的默认值，不是操作系统防火墙。若有人改 `Urls`，或设置 `ASPNETCORE_URLS` 去听别的地址，页面就会离开本机。**不要把 Studio 暴露到办公网或公网。** 安装说明写的是：5080 仅 `127.0.0.1`，不要改成对办公网监听。

页面静态文件和 `/healthz` 不要求登录。管理 API（`/api/v1`）除登录和登录页读取的账号模式接口外，要本机账号签发的令牌。三个角色是本机的 `admin` / `engineer` / `viewer`，不是域账号，也没有集中身份源。所以 5080 必须留在本机：一旦改成局域网监听，未登录就能打开页面，令牌也只是这台机器上的账号。

采集进程另有一个回环接口，默认 `http://127.0.0.1:5081`（可用 `GATEWAY_LOOPBACK` 关掉）。它提供运行态、日志尾和重载，**没有登录**。代码拒绝绑到 `127.0.0.1` / `localhost` 以外的地址，绑不上就忽略并继续采集。不要把它发布出去，也不要把它说成对外 API。

## FOCAS 留在机床网

南向是机床面板上的 FOCAS，常见 TCP **8193**，通常是明文工业协议。采集机到机床的访问只在机床网内。不要把 8193 做到办公网、端口映射或互联网上。网关是南北向的桥：南向只连已登记的机床，北向只连 MQTT。机床不要直接进公司办公网。分段方式见 [security.md](../security.md)。

## 口令

| 项 | 怎么放 |
| --- | --- |
| MQTT | `service.env` 里的 `MQTT_USER` / `MQTT_PASSWORD`（或页面上写的同名变量）。`install-service.bat` 注入服务进程。YAML 和发布历史里只有变量名。该文件不进 git、不进 zip |
| 现场登录 | `data\auth\bootstrap-password.txt` 的一次性密码，登录后必须修改（至少 8 位）。三个账号都改完后文件删除。口令以 PBKDF2 哈希留在 `data\auth\accounts.json`，文件里没有明文 |
| 本机演示 | `dotnet run` 且未设现场模式时，登录页预填 `admin` / `admin`（另有 engineer、viewer）。只适合 localhost。现场包不接受这组口令 |

`service.env` 和 `data\auth` 要备份，但不要提交进仓库。安装脚本在控制台只打印变量名，不打印值。日志会写引导密码 **文件路径**，不写密码本身；改密日志只有用户名。

忘记现场密码：停服务，删 `data\auth`，再启动，会重新生成引导文件。`data\published` 还在。

## 厂商库不在包里

git、镜像和 win-x64 zip 都没有 `Fwlib64.dll`。客户把授权的 64 位库放在 `Host.exe` 旁边。我们不拷走、不写进仓库。缺库时进程继续跑，`fanuc.focas` 为 offline，设备测试说明未找到库。

## 配置在文件里

事实来源是 `data\published` 下的 YAML。草稿在 `data\draft`，回滚靠 `data\revisions`。升级时这些目录、`data\auth`、`service.env` 和 `Fwlib64.dll` 都要留下。步骤见 [ops-field.md](../ops-field.md)。不要先删安装目录再解压。

产品不会在磁盘快满时报警。发布是直接写 YAML。备份建议见同一篇运维说明。

## 日志里有什么

默认级别是 Information。文件在 `logs\gateway-yyyyMMdd.log`（可用 `GATEWAY_LOG_DIR` 改目录），大约保留 14 天，不按大小截断。写失败不会把进程打退出。

Information / Warning 里会有：版本、操作系统、数据目录、采集配置路径、账号模式、MQTT 的 host、端口和 clientId、`Fwlib64.dll` 是否加载、FOCAS 连上或失败（设备 Id、地址端口、`EW_*` 等原文；连接失败大约 30 秒一条）、MQTT 连上、连不上或断开。FOCAS 和 MQTT 的失败原因可能含在这些行里。

默认 **不会** 把每条 MQTT 正文写入这份文件（正文只在 Debug）。日志里没有 MQTT 口令，也没有 Studio 口令。`data\runtime\studio.log` 记的是草稿、发布、回滚和设备测试结果（UTC），运行态页不显示它。Windows 事件日志只有服务停启一类记录，不记机床数据。

这不是审计系统。没有集中收集、没有告警平台、没有对多台网关的关联分析。

## 威胁上怎么说

这是 **一台采集机上的一个边缘进程**，不是 SIEM，也不是全厂安全产品。它不检测入侵，不代替防火墙，不管理其他网关。

实施时建议：

- 机床网、采集机、办公网分开。8193 只在机床网。
- Studio 保持 `127.0.0.1:5080`。不要为了「办公室也能打开页面」而改监听地址。
- MQTT 使用账号。Broker 不在本机或机床网时，打开配置里的 `mqtt.tls`。口令仍只在 `service.env`。
- 出站只保留 Broker 和已登记的机床。安装脚本没有改成受限 Windows 账号；若现场策略要求不用默认服务账号，由甲方的账号策略另行处理。本文不把这一点说成产品已经做到。
