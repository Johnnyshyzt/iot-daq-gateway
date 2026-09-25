# M1 发布清单

本文跟踪「可售卖的 M1」还差什么。Fake 验收以 Studio 为准：工程师只在 `src/Web`（由 `src/Host` 托管）里操作，不手写 YAML。根目录 `studio/` 已从产品路径删除。

验收口径见 [M1-prd.md](M1-prd.md)，页面见 [studio-ia.md](studio-ia.md)。OPC UA、多品牌、Fleet、许可证服务器不在本清单里展开实现。

## 已完成（Fake 零手写 YAML）

- [x] 一个 Host 进程托管 Api、Collector 和 shadcn Web（`http://127.0.0.1:5080`）
- [x] 本机演示登录：`admin` / `admin`，另有 `engineer` / `engineer`、`viewer` / `viewer`。页面标明只适合 localhost；现场包不把它们当作长期口令
- [x] 设备页新建、编辑、删除 `fanuc.fake`，并做连接测试（Fake 恒成功）
- [x] 点位表编辑；CSV 导入导出包含地址、类型、单位、倍率、死区、启用
- [x] MQTT：Broker、环境变量名、QoS、保留消息、TLS、主题模板，以及浏览器内 JSON / 主题预览
- [x] 校验、发布、回滚；失败时页面展示中文问题列表（含路径）
- [x] 发布写入 `data/published` 后，同一进程重载采集
- [x] 运行态在 `live` 下显示在线状态、最近观测，以及实际 MQTT 点位主题和状态主题
- [x] 采集按 `topicTemplate` / `statusTopic` 发布，默认仍是 `daq/{site}/{deviceId}/{point}` 与 `daq/{site}/{deviceId}/$status`
- [x] 接口失败有中文说明：网络不通、未登录、角色不够、校验 issues
- [x] 仓库不包含 FOCAS 厂商二进制

## 下一步（卖得出去之前）

- [ ] 真机 FOCAS：在 Windows x64 采集机旁路放置现场 `Fwlib64.dll`，确认 `fanuc.focas` 能采到 `state` / `alarm` / `program`。本仓库没有机床，此项未在真机上验收
- [x] 设备测试：`POST /api/v1/devices/{id}/test` 对 `fanuc.focas` 走与采集相同的 `cnc_allclibhndl3`。缺库、位数不对、连接失败返回中文原因；无 `Fwlib64.dll` 时明确失败且进程不崩。不把 TCP 通断当成握手成功
- [x] 现场包把 Studio 静态页、`data/seed` 和 Windows 服务自启放在同一台采集机上（zip 内有 `Host.exe`、`wwwroot`、`data/seed`、`install-service.bat`）。DLL 不进仓库、不进 zip
- [x] 密钥：MQTT 密码只走环境变量。`service.env` 由 `install-service.bat` 注入服务的 `MQTT_USER` / `MQTT_PASSWORD`（与 YAML 的 `usernameFromEnv` / `passwordFromEnv` 同名）
- [x] 账号：本机演示仍可用 `admin` / `admin` 等桩账号，页面标明只限 localhost。现场包生成一次性引导密码，首次登录必须修改。角色仍是本地 admin / engineer / viewer
- [ ] 运维：升级时保留 `data/published`、如何备份发布目录、日志目录和磁盘写满时的行为，写进现场说明

## 以后（不在 M1）

- [ ] 把 [management-api.md](../api/management-api.md) 里仍是草案的信封（`/config` 字段名、发布失败状态码、独立 diff）与现实现对齐，再给第二个客户端用
- [ ] OPC UA 北向
- [ ] 发那科以外的品牌适配器
- [ ] Fleet / 远程管理多台网关
- [ ] 真正拦截请求的许可证服务
- [ ] Excel 直接导入、历史曲线、程序下发
