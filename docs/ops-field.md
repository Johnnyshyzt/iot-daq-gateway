# 现场运维

本文给已经装好的 Windows x64 采集机：升级、备份、日志、磁盘写满。第一次解压、注册服务、放 `Fwlib64.dll` 见 [windows-install.md](windows-install.md)。故障对照见 [field-fault-guide.md](field-fault-guide.md)。

下文路径按 `C:\iot-daq-gateway\`。数据目录以启动日志里的 `Config data directory` 为准。默认就是安装目录下的 `data\`，不看服务的工作目录（服务经常停在 `C:\Windows\System32`）。设过 `HOST_DATA` 或 `STUDIO_DATA` 时，备份那个目录。

## 目录里什么该留

| 路径 | 升级时 |
| --- | --- |
| `Host.exe`、旁边的运行时文件、`wwwroot\` | 用同一份新 zip 整包换掉。不要只换 exe |
| `data\gateway.db`（以及 `-wal`、`-shm`） | **保留。** 草稿、已发布配置、回滚历史、目录、采样和通知通道都在这里。通道密钥也在这个文件里 |
| `data\mqtt-spool\` | **保留**（若存在）。还没发给 Broker 的报文。页面备份不含这个目录 |
| `data\published\`、`data\draft\` | 不再是运行副本。空库第一次启动才会导入。已有数据库时可以留着，升级不要靠它们恢复配置 |
| `data\runtime\studio.log` | 建议保留。配置操作记录，运行态页不显示它 |
| `data\seed\` | 可以随新 zip 换掉。数据库里已经有配置时，换 seed 不会改正在跑的配置 |
| `logs\` | **保留** |
| `data\auth\` | **保留。** 已修改的本机口令。删掉会重新生成一次性引导密码 |
| `service.env` | **保留。** MQTT 口令在这里。zip 里只有 `service.env.example` |
| `Fwlib64.dll` | **保留。** zip 里没有这个文件 |
| 安装目录根上的 `gateway.yaml`、`gateway.focas.yaml` | 只是参考，服务不读。被新 zip 盖掉没有关系 |
| `appsettings.json` | 新 zip 会换成现场模式（`AccountMode=field`）。口令不在这个文件里 |

MQTT 口令在 `service.env`，不在 YAML 里。见下文。

## 升级

1. 记下版本：日志第一行 `iot-daq-gateway …`，或安装目录 `VERSION.txt`。
2. 管理员执行 `sc stop IotDaqGateway`。
3. 先做下面的备份。
4. 新 zip 从对应版本的 GitHub Release 下载（`iot-daq-gateway-<version>-win-x64.zip`，见 [windows-install.md](windows-install.md)）。还没有 Release 时，用 Actions 的 `pack-win-x64` artifact。解压到临时目录。zip 里还有一层版本目录，源路径用里面直接含有 `Host.exe` 的那一层。不要先删掉 `C:\iot-daq-gateway\`，否则 `data\gateway.db`、`data\auth`、`logs`、`service.env` 和 `Fwlib64.dll` 会一起被删掉。

```bat
robocopy C:\temp\iot-daq-gateway-new C:\iot-daq-gateway /E /XD published draft revisions runtime auth logs /XF Fwlib64.dll service.env
```

`/XD` 按目录名排除。`robocopy` 结束码 0–7 表示拷贝完成，8 及以上才是失败。

5. 确认 `Fwlib64.dll`、`service.env`、`data\gateway.db` 还在。
6. 程序路径没变时，`sc start IotDaqGateway` 即可，服务里已经注入的环境还在。若重跑 `install-service.bat`，它会删掉并重建服务，再从 `service.env` 注入 `MQTT_USER` / `MQTT_PASSWORD`。重跑前确认 `service.env` 仍是填好的那份，不是 example。
7. 日志里版本号已变，并且写明采集读取数据库。浏览器打开 `http://127.0.0.1:5080`，运行态为 `live`。

页面发布会让同一进程重载数据库里的已发布配置，改配置不用重启。换程序、换 `Fwlib64.dll`、改 `service.env` 后要重新注入并重启服务。

## 备份与恢复

采集真正用的是 `data\gateway.db`（SQLite 还有旁边的 `-wal` 和 `-shm`）。停服务后再拷，避免拷到写了一半的 WAL。同一次把 `data\auth` 和 `service.env` 也拷走。`service.env` 里是 Broker 口令，备份不要提交进 git。

```bat
set DEST=D:\backups\iot-daq-gateway\2026-09-25
mkdir %DEST%
copy /Y C:\iot-daq-gateway\data\gateway.db %DEST%\
copy /Y C:\iot-daq-gateway\data\gateway.db-wal %DEST%\
copy /Y C:\iot-daq-gateway\data\gateway.db-shm %DEST%\
robocopy C:\iot-daq-gateway\data\auth %DEST%\auth /E
copy /Y C:\iot-daq-gateway\service.env %DEST%\
```

`-wal` 或 `-shm` 不存在时，`copy` 会提示找不到文件，可以忽略。恢复：

1. `sc stop IotDaqGateway`
2. 用备份换掉 `data\gateway.db`，以及备份里有的 `-wal` / `-shm`。不要留下旧库的 WAL 配新的主文件
3. `sc start IotDaqGateway`

进程还在时，页面「回滚」从数据库里的修订历史找回，不必先拷文件。历史最多 30 条。

管理员也可以在「系统」页下载一份一致的 SQLite 快照，或从这份文件恢复。恢复前会把当前库留成 `data/gateway.db.bak`。页面备份不含 `data/auth`，恢复不会改掉正在使用的登录口令。PostgreSQL 不走这个按钮，仍在数据库服务器上备份。停服务后按上面的文件拷贝，仍然是现场换机时更完整的做法（连同 `data/auth` 和 `service.env`）。

配置里只有环境变量名（`usernameFromEnv` / `passwordFromEnv`）。Broker 口令在 `service.env` 那份备份里。`data\auth` 是本机登录哈希，不是 MQTT 口令。导出的 YAML 同样只有变量名。

`gateway.db` 已经损坏时，用上面的整库备份换回去再启动。不要把旧的 `data\published` 当成运行副本；只有在数据库文件不存在、需要重新导入时，那份 YAML 才会被读一次。

## 环境变量（MQTT 口令）

复制 `service.env.example` 为 `service.env`，填写 `MQTT_USER` 和 `MQTT_PASSWORD`。名字要和已发布配置里的 `usernameFromEnv` / `passwordFromEnv` 一致。种子配置默认就是这两个名字。明文不进数据库、不进 revision、不进 zip。

`install-service.bat` 调用 `service-env.ps1`，把 `service.env` 写成服务注册表 `Environment`（`REG_MULTI_SZ`）。`run-console.bat` 用同一份文件给前台进程。空行、`#` 注释、值为空的行不注入。脚本只打印变量名。

改完 `service.env` 后，以管理员再运行一次 `install-service.bat`（会重建服务并重新注入）。只改文件、不重跑脚本，正在运行的进程看不到新值。启动日志出现 `MQTT connected to …` 才表示连上。

细节见 [windows-install.md](windows-install.md)。`data\seed\secrets.env.example` 只是变量名注释，进程不加载它。

## 日志

| 文件 | 内容 |
| --- | --- |
| `logs\gateway-yyyyMMdd.log` | 版本、数据目录、采集路径、MQTT 地址和 clientId、`Fwlib64.dll` 是否加载、FOCAS / MQTT 失败。本地日期。运行态页的日志尾读这份 |
| `GATEWAY_LOG_DIR` | 改上面那个目录。不设则在 `Host.exe` 旁的 `logs\` |
| `data\runtime\studio.log` | 发布、改草稿。UTC。运行态页不读 |
| 系统事件日志 | 服务停启、反复重启。不记录机床数据 |

文件日志保留约 14 天（按文件最后写入时间删除），**不按大小截断**。写日志失败会被吞掉，不会因此把网关打退出。FOCAS 连接失败大约 30 秒打一行，不是每个扫描周期都有。

## 磁盘写满

代码里没有「磁盘已满」专用分支，也没有在满盘机器上做过演练。下面是阅读当前写入路径后的结论。

- 发布和保存草稿写 SQLite。事务失败时已发布槽位保持上一次成功的内容。磁盘在写入中途满掉时，库文件或 WAL 可能需要用备份换回。
- 进程还活着、重载失败时，内存里继续用上一份已经跑起来的采集会话，页面可能看到「服务器内部错误」。日志里对应 `Unhandled host error`。磁盘满到日志也写不进去时，这行可能不在文件里。
- 下次启动读 `data\gateway.db`。库打不开时 Host 起不来，服务会按安装脚本的失败策略重启，空转不会腾出空间。
- 文件日志写失败不退出进程。`studio.log` 追加失败会让当次保存或发布失败。
- 修订历史最多留 30 条，更旧的会从 `config_revisions` 删掉。同盘更常见的占用是 `logs\`（只按天数删）、采样历史以及 Windows 和其他软件。

处理：

1. 腾出安装盘空间。可以拷走并删除过旧的 `logs\gateway-*.log`。不要为了腾空间删除 `data\gateway.db`、`data\auth` 或 `service.env`。
2. Host 起不来或发布后采集异常时，用备份换回 `data\gateway.db`（以及当时的 `-wal` / `-shm`），再 `sc start`。进程还在时优先用页面回滚。
3. 建议在安装盘剩余空间低于 1 GB 时告警（任务计划或现有监控即可）。产品自己不会报警。
