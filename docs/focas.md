# FOCAS 说明

本仓库 **不包含** FANUC FOCAS 原生库、头文件或授权文件。厂商二进制必须留在 git 与容器镜像之外。

## 第一期承诺路径：Windows x64 采集机

真实以太网 FOCAS **只在 Windows x64 进程上作为生产路径**：

1. 从 FANUC 授权渠道取得 **64 位** `Fwlib64.dll`（及厂商要求的依赖）。
2. 把 `Fwlib64.dll` 放到 Host 进程旁边（与 `Host.exe` / 发布目录同一文件夹）。不要提交进 git。
3. 复制 `configs/examples/gateway.focas.yaml`，改机床 `host` / `port`（常见 **8193**）/ `timeoutMs`。YAML 在进程外，改 IP **不必重新编译或重建镜像**。
4. 启动网关：现场 zip 用 `install-service.bat` 注册 Windows 服务（开机自启），或 `run-console.bat` 前台验证。适配器会调用 `cnc_allclibhndl3`；之后每轮扫描读状态 / 报警 / 程序号，失败则释放句柄并在后续扫描重连。

发布与开机自启步骤见 [windows-install.md](windows-install.md)。`Fwlib64.dll` 必须与 `Host.exe` 放在同一目录；**不要**提交进 git 或打进镜像。

**不要把 Linux Docker 当作生产 FOCAS 路径。** 官方库以 Windows `Fwlib64.dll` 为主；Compose 镜像是 Fake 演示 / MQTT 附属，不会、也不应把厂商 `.so` 打进镜像层。

## 现场机床检查单

- CNC 已开通以太网 FOCAS（嵌入式以太网或以太网板 / 选件，以机床手册为准）。
- 面板 IP 与 FOCAS 端口（常见 TCP **8193**）可达：采集机 → 机床，不要把 8193 暴露到办公网。
- 网关进程为 **x64**；`Fwlib64.dll` 也必须是 64 位。缺库、错位数时适配器为 `offline` 并写出原因，**进程不退出**。

## 采集点位（与 Fake 相同）

| 点位 | 来源 | 取值 |
| --- | --- | --- |
| `state` | `cnc_statinfo`（16i/0i/30i 的 `ODBST`） | `IDLE` / `RUNNING` / `ALARM` |
| `alarm` | `cnc_rdalmmsg`（无报文时用状态字） | 报警号，正常为 `0` |
| `program` | `cnc_rdprgnum` / `cnc_rdprgnumo8` | `O0001` 形式 |

连接：`cnc_allclibhndl3`（超时单位为秒，由 YAML `timeoutMs` 向上取整）。读超时：`cnc_settimeout`。释放：`cnc_freelibhndl`。结构体按 `fwlib64.h` **`#pragma pack(4)`** 布局，面向 x64 `Fwlib64.dll`。

缺库、连不上或会话断开时 `$status` 为 `offline`（日志含 `Fwlib64` / `EW_SOCKET` 等）。**下一轮扫描会再次 `cnc_allclibhndl3`，不会连一次失败就永久死掉。**

## 代码位置

- 接口：`ISouthboundAdapter`（与 Fake 相同）
- 实现：`src/Adapters.Fanuc/Focas/FocasFanucAdapter.cs`
- P/Invoke：`src/Adapters.Fanuc/Focas/FocasNative.cs`
- 配置：`adapter: fanuc.focas`，见 `configs/examples/gateway.focas.yaml`

无硬件 / 无 DLL 的回归：`dotnet test`（缺库 → offline、YAML 可加载、进程不崩）。

## 开发建议

日常开发使用 `fanuc.fake`。只有在 Windows 采集机上已放置授权 `Fwlib64.dll` 后，再切换 `fanuc.focas`。
