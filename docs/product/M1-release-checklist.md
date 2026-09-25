# M1 发布清单

本文跟踪「可售卖的 M1」还差什么。Fake 验收以 Studio 为准：工程师只在 `src/Web`（由 `src/Host` 托管）里操作，不手写 YAML。根目录 `studio/` 已从产品路径删除。

验收口径见 [M1-prd.md](M1-prd.md)，页面见 [studio-ia.md](studio-ia.md)。OPC UA、多品牌、Fleet、许可证服务器不在本清单里展开实现。

## 已完成（Fake 零手写 YAML）

- [x] 一个 Host 进程托管 Api、Collector 和 shadcn Web（`http://127.0.0.1:5080`）
- [x] 本地桩登录：`admin` / `admin`，另有 `engineer` / `engineer`、`viewer` / `viewer`
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

- [ ] 真机 FOCAS：Windows x64 旁路放置现场 `Fwlib64.dll`，`fanuc.focas` 能采到 `state` / `alarm` / `program`。设备测试接口今天只探测 TCP，完整握手仍在采集进程里
- [ ] 现场包把 Studio 静态页、`data/seed` 和 Windows 服务自启放在同一台采集机上走通（DLL 不进仓库、不进镜像）
- [ ] 密钥：MQTT 密码只走环境变量。把 Windows 服务如何注入 `MQTT_USER` / `MQTT_PASSWORD` 写进安装说明，并在现场包里可操作
- [ ] 账号：本地桩够做演示。出厂前要明确「仅本机操作员」，或换成可改密码的本地账号，不要把默认 `admin` / `admin` 交到客户现场
- [ ] 运维：升级时保留 `data/published`、如何备份发布目录、日志目录和磁盘写满时的行为，写进现场说明

## 以后（不在 M1）

- [ ] 把 [management-api.md](../api/management-api.md) 里仍是草案的信封（`/config` 字段名、发布失败状态码、独立 diff）与现实现对齐，再给第二个客户端用
- [ ] OPC UA 北向
- [ ] 发那科以外的品牌适配器
- [ ] Fleet / 远程管理多台网关
- [ ] 真正拦截请求的许可证服务
- [ ] Excel 直接导入、历史曲线、程序下发
