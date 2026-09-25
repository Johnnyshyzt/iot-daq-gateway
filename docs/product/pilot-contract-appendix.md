# 试点合同附件（草稿）

本文可整段贴进试点合同或工作说明书，作为软件范围附件。价格、支持天数不在这里写，见报价单，由 Johnny 填写。

本仓库没有自有发那科机床，没有 `Fwlib64.dll`，也没有填完的现场记录。附件不把 Fake、TCP 端口通了、或一次设备测试握手写成 FOCAS 已验证。出发前与现场怎么分开，以 [pilot-acceptance.md](pilot-acceptance.md) 为准。

## 填写

| 项 | 填写 |
| --- | --- |
| 甲方（客户） | |
| 乙方（供方） | |
| 站点名称 | |
| 试点 CNC 台数 | （首台试点建议 **1**） |
| 采集机（Windows x64，一台） | |
| 软件版本 | （当前 Host 包：GitHub Release `v0.4.0`，`iot-daq-gateway-0.4.0-win-x64.zip`；此后用同规则的更新 Host 包） |
| 试点起止 | |
| 签署日期 | |

本试点是 **一台** Windows x64 采集机上的 **一个** Host。台数栏建议填 1。同一台采集机上再挂发那科设备，仍不是 Fleet；是否纳入本次人天，只由报价单约定。

## 1. 软件交付范围

乙方交付的软件是本仓库 Apache-2.0 许可下的一个 Windows x64 Host 进程，内含采集、管理 API 和 Studio 页面。现场浏览器打开本机 `http://127.0.0.1:5080`。

范围内包括：

- **Fake 路径。** `fanuc.fake` 设备、点位、MQTT、校验、发布、回滚、运行态。工程师在页面里完成，不手写 YAML。采集读 `data/published`，同一进程重载。
- **MQTT 北向 JSON。** 点位主题 `daq/{site}/{deviceId}/{point}`，状态主题 `daq/{site}/{deviceId}/$status`。
- **发那科南向适配器源码。** 设备适配器只能是 `fanuc.fake` 或 `fanuc.focas`。`fanuc.focas` 在 Windows x64 上对客户自备的 `Fwlib64.dll` 做 P/Invoke。缺库时进程继续跑，设备测试给出中文失败，不会因为 TCP 通了就显示成功。
- **配置。** YAML 是单一事实源。页面改草稿（`data/draft`），校验通过后发布到 `data/published`。历史在 `data/revisions`。数据库不是配置的唯一副本。

本附件 **不出售许可证密钥**。M1 的许可证桩不拦截请求，也没有许可证服务器。商业部分是下面的实施与支持，金额见报价单。

## 2. 本试点 / M1 不做

下列各项不在本附件的软件交付里，也不在第一次 FOCAS 人天里。报价单未单列之前，乙方不实施：

- OPC UA 或其他北向（本期只有 MQTT JSON）
- 发那科以外的品牌
- Fleet，或远程管理多台网关
- 真正拦截请求的许可证服务器
- Excel 直接导入、历史曲线、时序库或历史数据库。点位可以导入导出 CSV；Excel 需另存为 CSV
- 把管理 API 改写成另一套信封，供第二个客户端使用。现有 Studio 用的接口保持现状
- Linux 上的生产 FOCAS。Docker / Linux 只用于 Fake 演示或附属 MQTT，不是生产路径
- CNC 程序下发。程序相关能力默认关闭

## 3. 客户前提

甲方在约定的采集机上提供：

- **Windows x64** 机器。现场不需要安装 .NET SDK，也不需要 git 检出本仓库。
- 采集机到 CNC 的 FOCAS 端口只在 **机床网** 内可达。常见端口是 TCP **8193**，以机床实际配置为准。不要把 8193 暴露到办公网或互联网。
- 客户自备的 **64 位** `Fwlib64.dll`（及该库自己要求的依赖），放在 `Host.exe` 旁边。乙方不提供、不随 zip 分发、不写入仓库。
- 若北向需要账号，甲方提供 MQTT Broker，以及写入 `service.env` 的 `MQTT_USER` / `MQTT_PASSWORD`。明文不进 YAML。

前提未齐时，只做第 4 节的软件交付验收，不开始现场 FOCAS。

## 4. 验收

验收分成两条，不能互相代替。

### （A）软件交付验收：出发前 Fake 清单

在乙方或实验室完成，不使用甲方机床，不放置 `Fwlib64.dll`。

清单以 [pilot-acceptance.md](pilot-acceptance.md)「出发前：Fake 清单」为准，须全部通过。通过只说明 Host 与 Studio 在 Fake 路径上可交付：页面能配 `fanuc.fake`、能发布、MQTT 能订到相位变化的 `state` / `alarm` / `program`，缺库时 `fanuc.focas` 诚实失败。Fake 的 `state` 按大约 60 秒相位变化，`program` 为 `O0001`。这是桩，不是机床。

（A）未全过，不去现场做 FOCAS。

### （B）第一次 FOCAS：可选、单列的实施项

（B）不是（A）的一部分。只在（A）全过，且甲方已提供采集机、机床、机床网和 64 位 `Fwlib64.dll` 之后，按报价单上的人天实施。

成功标准是 [pilot-acceptance.md](pilot-acceptance.md) 的 **现场记录表** 填写完成，而不是设备测试出现握手成功。测试成功原文含「FOCAS 握手成功（cnc_allclibhndl3」以及「不代表采集已经保持在线」。还要看到运行态里随机床变化的 `state`（`IDLE` / `RUNNING` / `ALARM`）、`alarm`、`program`（`O` 加程序号），并与 Fake 的 60 秒相位区分开。失败时保留日志里的 `cnc_allclibhndl3` / `EW_SOCKET` / `EW_PROTOCOL` / `EW_VERSION` 原文。

没有填完的表，验收单、销售材料和本附件都不写「FOCAS 已在真机验证」。

## 5. 交付物

- GitHub Release 上的 Windows x64 Host 包。当前为 [v0.4.0](https://github.com/Johnnyshyzt/iot-daq-gateway/releases/tag/v0.4.0) 的 `iot-daq-gateway-0.4.0-win-x64.zip`。此后的 Host 包沿用 `iot-daq-gateway-<version>-win-x64.zip`。包内有 `Host.exe`、`wwwroot`、`data/seed` 和安装脚本，**不含** `Fwlib64.dll`。更早的 `v0.3.0` 是只有 Gateway 的历史包，不作为本附件的交付物。
- 安装：[windows-install.md](../windows-install.md)。升级与备份：[ops-field.md](../ops-field.md)。故障对照：[field-fault-guide.md](../field-fault-guide.md)。
- 现场 zip 首次登录用 `data\auth\bootstrap-password.txt` 里的一次性密码，登录后立刻修改（至少 8 位）。三个角色仍是本机账号。`admin` / `admin` 只属于本机演示模式，不得留在甲方机器上。

## 6. 支持与升级

| 项 | 约定 |
| --- | --- |
| 支持 / 质保期限 | ________（Johnny 填写。本附件不写天数） |
| 支持范围 | 已安装的这一台 Host：升级协助、故障通讯、版本说明 |
| 不含 | 新品牌、OPC UA、Fleet、许可证服务器，以及第 2 节其余各项 |

升级时保留 `data\published`（采集读这里），以及 `data\draft`、`data\revisions`、`data\auth`、`service.env` 和现场的 `Fwlib64.dll`。不要先删掉安装目录再解压。步骤见 [ops-field.md](../ops-field.md)。

## 7. 责任与诚实

- 乙方不声称本仓库、本 zip 或本附件已经在发那科真机上完成 FOCAS 采集验证。截至起草本附件，仓库内没有这类记录。
- 厂商 `Fwlib64.dll` 由甲方提供并放置。乙方不把该文件写入 git、zip 或镜像，也不从现场拷走（甲方另有书面授权时除外，且仍不得写入本仓库）。
- 「设备测试握手成功」不是现场验收。没有填完的现场记录表，双方都不对第三方写成已在真机验证。
- FOCAS 是机床网上的工业协议。甲方负责机床网与办公网的隔离。乙方不把 8193 发布到办公网当作交付内容。

## 8. 价格

本附件不写人民币或其他金额。软件许可为 Apache-2.0。第一次 FOCAS 人天、现场实施、年维保的价格 **见报价单 / Johnny 填写**。计价轴（按网关现场或按 CNC 台数）在报价单选定之前，合同写「计价轴待定」。

## 9. 签署

| | 甲方 | 乙方 |
| --- | --- | --- |
| 名称 | | |
| 授权代表 | | |
| 签字 | | |
| 日期 | | |

双方确认：第 4 节（A）是软件交付验收；（B）仅在前提齐备且报价单单列之后实施。
