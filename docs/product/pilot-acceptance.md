# 试点验收（无自有发那科机床）

M1 是 Open Core：现场一台 Windows x64 Host，浏览器即 Studio。本仓库 **没有** 自有发那科机床，**没有** `Fwlib64.dll`，也 **没有** 一份真实 CNC 的采集记录。下文用来约束销售和上门，避免把 Fake 或「TCP 通了」写成 FOCAS 已验证。

## 没有机床时可以承诺的

- 工程师只在 `http://127.0.0.1:5080` 里完成：`fanuc.fake` 设备、点位、MQTT、校验、发布、回滚。采集读 `data/published`，同一进程重载。
- 订阅 `daq/#` 能看到 Fake 的 `state` / `alarm` / `program` 和每轮 `$status`。`state` 按大约 60 秒相位在 `IDLE`、`RUNNING`、`ALARM` 之间变化，`program` 为 `O0001`。这是桩，不是机床。
- Windows x64 包里 FOCAS 适配器已经接上。不放库时进程继续提供页面。设备测试对 `fanuc.focas` 走 `cnc_allclibhndl3`，缺库时中文失败（「未找到 Fwlib64.dll」），不会因为 TCP 通了就显示成功。这条用无库测试覆盖，没有机床握手记录。
- 设备测试对 Fake 返回「Fake 适配器握手成功（未连接真实机床）」。

## 有一份现场记录之前不能写进材料的

- 「已在发那科真机采到 `state` / `alarm` / `program`」
- 「设备测试握手成功 = 现场验收已完成」。成功原文写明只说明此刻能连上，不代表采集保持在线
- 任何自造的 `Fwlib64.dll`、自填的 CNC 型号、或没有现场日志/截图的联调结论
- OPC UA、多品牌、Fleet、许可证服务器（不在 M1）

设备测试和采集都调用 `cnc_allclibhndl3`。没有 `Fwlib64.dll` 时测试失败。有库且机床可达时，测试成功仍要再看运行态里的 `state` / `alarm` / `program` 和 `logs\gateway-yyyyMMdd.log`。

## 出发前：Fake 清单（必须全过）

在本公司或实验室完成，不使用客户机床，不使用私造的 FOCAS 库。

- [ ] 按 [windows-install.md](../windows-install.md) 解开 win-x64 zip。`run-console.bat` 或服务能起来，日志第一行有版本号
- [ ] 本机 `http://127.0.0.1:5080` 能登录。现场 zip 用 `data\auth\bootstrap-password.txt` 并立刻改密。`admin` / `admin` 只属于本机演示模式，见 [field-fault-guide.md](../field-fault-guide.md)
- [ ] 设备为 `fanuc.fake`，连接测试成功原文含「未连接真实机床」
- [ ] 点位含 `state`、`alarm`、`program`；校验无 **错误**；发布成功
- [ ] 运行态 `mode` 为 `live`，该设备 online
- [ ] MQTT 能订到 `daq/#`。`state` 随时间按相位变化，而不是停在某一台真实机床的值
- [ ] Broker 要求账号时：`service.env` 里有 `MQTT_USER` / `MQTT_PASSWORD`，`run-console.bat` 或 `install-service.bat` 已加载它，日志出现 `MQTT connected to`。见 [ops-field.md](../ops-field.md)
- [ ] 页面回滚一次，采集仍在跑
- [ ] 按 [ops-field.md](../ops-field.md) 做一次升级演练：`data\published` 还在，版本号已变
- [ ] **不放置** `Fwlib64.dll`，把一台设备改成 `fanuc.focas` 并做连接测试：页面失败原文含「未找到 Fwlib64.dll」，进程不退出。发布后设备 offline。这一条只证明「缺库时诚实失败」

出发前清单未全过，不去客户现场做 FOCAS。

## 现场：FOCAS 清单（客户机床）

单独一次实施，不把出发前的 Fake 结果算进来。

- [ ] 采集机是 Windows x64，跑的是现场 zip 里的 `Host.exe`，不是 Linux 容器
- [ ] 客户自备的 **64 位** `Fwlib64.dll` 在 `Host.exe` 旁边。我们不拷走、不写入仓库、不打进安装包。放置后重启服务
- [ ] 日志为 `Fwlib64.dll loaded`，而不是 `not loaded`
- [ ] 采集机到机床 FOCAS 端口的网络只在机床网内（常见 TCP 8193）。不要把 8193 暴露到办公网
- [ ] 设备测试成功原文含「FOCAS 握手成功（cnc_allclibhndl3」以及「不代表采集已经保持在线」。不能只凭这一句完成验收
- [ ] 发布 `fanuc.focas` 之后，运行态 `$status` 为 online，并能看到随机床变化的 `state`（`IDLE` / `RUNNING` / `ALARM`）、`alarm`、`program`（`O` 加程序号）。与 Fake 的 60 秒相位不同
- [ ] 失败时留下日志里的 `cnc_allclibhndl3` / `EW_SOCKET` / `EW_PROTOCOL` / `EW_VERSION` 原文，不改写成已连通

## 现场记录（有这一份之后才能引用「真机」）

| 项 | 填写 |
| --- | --- |
| 日期 | |
| 站点、采集机 | |
| 网关版本（日志首行或 `VERSION.txt`） | |
| CNC 型号 / 系统版本 | 客户提供的原文 |
| 机床 IP、FOCAS 端口 | |
| 网络（采集机网段 → 机床网段） | |
| `Fwlib64.dll` 来源 | 只写「客户提供」及客户同意留下的版本备注。不附文件、不写进 git |
| 观测 | `state`、`alarm`、`program` 各留一条（运行态截图或 MQTT 原文） |
| 日志 | `Fwlib64.dll loaded`，以及失败时的 `EW_*` 行 |
| 记录人 | |

没有填完的表，销售材料、验收单和本清单都不写「FOCAS 已在真机验证」。

## 合同口径

软件交付的范围是 Host 与 Studio，以及出发前 Fake 清单。**第一次 FOCAS 联调单列实施人天**，在客户提供机床、网络和 `Fwlib64.dll` 的前提下进行。不在本文写价格。联调人天不包括 OPC UA、其他品牌和远程 Fleet。

## 相关文档

- 可贴进试点合同的附件草稿：[pilot-contract-appendix.md](pilot-contract-appendix.md)
- 给买家或集成商的一页：[sales-one-pager.md](sales-one-pager.md)
- 销售和实施可复述的安全口径：[security-narrative.md](security-narrative.md)
