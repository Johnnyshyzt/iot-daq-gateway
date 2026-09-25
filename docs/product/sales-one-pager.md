# 销售一页

给买家或集成商的短稿。没有案例、没有机床型号、没有采集指标。价格见报价单，不在本文。真机能承诺什么，以 [pilot-acceptance.md](pilot-acceptance.md) 为准。

## 这是什么

一台放在机床旁边的 Windows 采集机：一个 Host 进程里有采集、管理 API 和浏览器里的 Studio。南向是发那科（`fanuc.fake` 或 `fanuc.focas`），北向是 MQTT JSON（`daq/{site}/{deviceId}/{point}` 和 `$status`）。配置在页面里改，发布后的 YAML（`data/published`）是事实来源。工程师打开本机 `http://127.0.0.1:5080`，不用手写文件。

## 为什么是 Open Core

仓库里的采集、API 和 Studio 是 Apache-2.0，可以下载、阅读、自己跑 Fake。商业部分不是许可证密钥：M1 没有会拦截请求的许可证服务器。卖的是 **第一次 FOCAS 现场联调** 和 **已安装网关的支持**。边界见 [open-core.md](open-core.md)，价位空表见 [pricing-one-pager.md](pricing-one-pager.md)。

## 怎么看（约 10 分钟）

按 [fake-demo-script.md](fake-demo-script.md) 在本机走 `fanuc.fake`：登录、设备、点位、MQTT、发布、运行态。`state` 按大约 60 秒相位在 `IDLE`、`RUNNING`、`ALARM` 之间变化，`program` 为 `O0001`。

口头要说清：**Fake 不是发那科机床。** 本仓库没有自有 CNC，没有 `Fwlib64.dll`，没有填完的现场记录。不要把这次演示写成已经采到真机。

## 真机时买方要带什么

- 一台 Windows x64 采集机（现场包自带运行时，不需要 .NET SDK）
- 机床网上到 CNC FOCAS 端口的连通（常见 TCP 8193）。不要把 8193 暴露到办公网
- 买方自备的 **64 位** `Fwlib64.dll`，放在 `Host.exe` 旁边。我们不随仓库或 zip 提供这个文件
- 若北向要账号：MQTT Broker，口令放在 `service.env`，不写进页面

出发前的 Fake 清单没全过，不去现场做 FOCAS。第一次真机联调是单独的实施项，成功要看填完的现场记录表，不是只看「设备测试握手成功」。

## V1 不卖

OPC UA、发那科以外的品牌、Fleet / 远程管理多台网关。也不卖 Excel 直接导入、历史库、Linux 上的生产 FOCAS。点位可以走 CSV。

## 下一步

1. 下载当前 Host 包：[v0.4.0](https://github.com/Johnnyshyzt/iot-daq-gateway/releases/tag/v0.4.0) 的 `iot-daq-gateway-0.4.0-win-x64.zip`（不含 `Fwlib64.dll`）。`v0.3.0` 是更早的 Gateway-only 包，不要当成现在的 Host。安装见 [windows-install.md](../windows-install.md)。
2. 用 Fake 自己走一遍上面的 10 分钟。现场 zip 用一次性引导密码并马上改掉；`admin` / `admin` 只属于本机演示。
3. 要进试点：先约定软件交付（Fake 清单），再把第一次 FOCAS 写进人天。附件草稿见 [pilot-contract-appendix.md](pilot-contract-appendix.md)。价格见报价单 / Johnny 填写。
