# 现场故障一页

采集机本机浏览器：`http://127.0.0.1:5080`。日志：`logs\gateway-yyyyMMdd.log`。升级与备份见 [ops-field.md](ops-field.md)。安装与 `service.env` 见 [windows-install.md](windows-install.md)。真机尚未在本仓库留下验收记录，见 [product/pilot-acceptance.md](product/pilot-acceptance.md)。

| 现象 | 原因 | 处置 |
| --- | --- | --- |
| 设备测试失败，原文「未找到 Fwlib64.dll」；或日志有 `Fwlib64.dll not loaded` / `Missing Fwlib64.dll` | 进程旁没有 **64 位** `Fwlib64.dll`。进程继续跑，`fanuc.focas` 为 offline。测试走 `cnc_allclibhndl3`，不会因为 TCP 通了就成功 | 把客户授权的 64 位库放到与 `Host.exe` 同一目录，文件名 `Fwlib64.dll`，然后 **重启服务**（加载结果缓存在本进程里）。不要提交这个文件 |
| 原文「位数不对」或日志含 `BadImageFormat` | 放成了 32 位库 | 换成 64 位 `Fwlib64.dll`，重启服务 |
| 原文「加载 Fwlib64.dll 失败」 | 文件在，依赖没加载上 | 按该库自己的说明补依赖（常见是厂商要求的 VC 运行库）。本仓库不附带 redist |
| 原文含「只支持 Windows x64」 | 不是 Windows x64 进程 | 换现场 win-x64 包。Linux 镜像不是生产 FOCAS 路径 |
| 测试失败，原文「FOCAS 握手失败：无法连接机床…（EW_SOCKET）」 | 库已加载，`cnc_allclibhndl3` 没连上。常见是 IP/端口、选件未开、路由不通 | 确认已发布配置里的地址和端口（常见 **8193**），采集机到机床网出站可达。下一轮扫描会重连，进程不退出。见 [focas.md](focas.md)、[security.md](security.md) |
| 测试成功，原文含「FOCAS 握手成功」和「不代表采集已经保持在线」 | 此刻握手完成并已释放句柄。还不是持续采集 | 再看运行态 `$status` 和 `state` / `alarm` / `program`。没有现场记录之前，不要写成已验收，见 [product/pilot-acceptance.md](product/pilot-acceptance.md) |
| 测试成功，原文「Fake 适配器握手成功（未连接真实机床）」 | 设备是 `fanuc.fake` | 这是销售/开发桩。`state` 按大约 60 秒相位变化 |
| 日志反复 `MQTT connect failed … Retry in 5s`，并有 `MQTT not connected; drop publish` | Broker 拒绝或连不上。采集继续，当次 MQTT 被丢掉 | 核对已发布的地址、端口、`clientId`，以及 `service.env` 里的 `MQTT_USER` / `MQTT_PASSWORD`。改文件后要管理员重跑 `install-service.bat`。直到日志出现 `MQTT connected to` |
| 两台网关轮流掉线，日志 `MQTT disconnected` | `clientId` 相同 | 每台用不同的 `clientId`，发布后再看 |
| 发布页「错误 · 路径：中文说明」，提示「草稿未通过校验，未发布」 | 草稿不合法。`data\published` 不会被这次发布改掉 | 按路径改草稿。常见：至少一台设备、设备地址、启用设备至少一个启用点位、Broker 地址、Client Id、主题模板含 `{deviceId}` 和 `{point}`、适配器只能是 `fanuc.fake` 或 `fanuc.focas`、Id 重复。`警告` 不挡发布 |
| 发布或保存变成「服务器内部错误」 | 不是校验失败。磁盘满时写 YAML 会走到这里 | 见 [ops-field.md](ops-field.md) |
| 点了发布但角色是 viewer | 「当前角色无权修改配置」 | 用 `engineer` 或 `admin` |
| 现场包用 `admin` / `admin` 登不上 | 现场模式不接受演示口令。一次性密码在 `data\auth\bootstrap-password.txt`，登录后必须改密（至少 8 位）。三个角色仍是本机账号 | 打开该文件登录并改密。三个账号都改完后文件会删除。忘记密码：停服务，删 `data\auth`，再启动，会重新生成引导文件；`data\published` 还在 |
| 开发机演示却要求引导密码 | 数据目录里的账号文件和模式不一致 | 登录页会说明。现场交付前停服务，删除 `data\auth` 后重启。演示模式（`dotnet run`，未设 field）预填 `admin` / `admin`，只适合 localhost |
