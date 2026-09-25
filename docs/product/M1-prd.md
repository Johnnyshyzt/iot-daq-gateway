# M1 PRD — 可视化采集网关（可售卖版）

本文是 M1 的产品范围。采集（Collector）、管理 API（Api）和页面（Web，shadcn-admin）都在本仓库，由 `src/Host` 一个进程跑起来。JSON Schema 与示例配置树也在这里。以后可以再拆仓，当前没有第二个仓库。还没做完的售卖项见 [M1-release-checklist.md](M1-release-checklist.md)。

## 产品定调（已锁定）

- **Open Core**：Runtime 与 Studio 都在本 monorepo，许可证 Apache-2.0
- **部署**：一个 Host 进程。页面发布 YAML 后，同一进程重载采集。采集留在能访问机床的现场机器上
- **配置源**：文件系统为单一事实源（YAML）；DB 仅作可选缓存，不可成为唯一真相
- **设备**：Fanuc FOCAS 做透（含 Fake）；其他品牌不进 M1
- **北向**：仅 MQTT JSON；OPC UA 明确二期

架构边界见 [open-core.md](open-core.md)。

## 成功标准（验收）

工程师在 Studio UI 中、**零手写 YAML** 完成：

1. 添加一台 Fanuc（或 Fake）设备
2. 配置/导入点位
3. 配置 MQTT Broker 与 Topic 映射
4. 校验 → 发布
5. 运行态看到在线状态与最近观测；MQTT 能收到对应主题

验收时南向使用 `fanuc.fake`（无机床）或 `fanuc.focas`（Windows x64 + 现场 `Fwlib64.dll`）。北向主题仍是 `daq/{site}/{deviceId}/{point}` 与 `daq/{site}/{deviceId}/$status`。

## 范围 In

- 配置契约 schema + 校验（`schemas/`，示例 `configs/examples/v1/`）
- Management API：config CRUD、validate、publish、rollback、device test、runtime status（合同见 [management-api.md](../api/management-api.md)）
- Studio 页面：概览、设备、点位、MQTT、发布、运行态、用户（admin / engineer / viewer），见 [studio-ia.md](studio-ia.md)
- 草稿 → 校验 → 发布 → 回滚；config revision（content hash）
- 敏感项引用密钥/环境变量，不落明文密码到配置仓库
- 许可证桩：无 license 时 Runtime 与 Studio 都可以运行；M1 不拦截管理 API

## 范围 Out（M1 不做）

- OPC UA、多品牌适配器、Fleet 云端、组态大屏、长期时序库、程序写入实开
## 能力落在哪

| 能力 | Collector | Api + Web |
| --- | --- | --- |
| YAML | 读 `data/published`，或 `--config` 覆盖 | 写草稿并发布 |
| Fake + FOCAS 桩 | ✅ | 连接测试：Fake 成功，FOCAS 只探测 TCP |
| MQTT Sink | ✅ | 编辑 Broker 与环境变量名 |
| 可视化编辑 / 发布 | 发布后进程内重载 | ✅ |
| 点位 CSV | — | ✅；Excel 另存为 CSV |
| 角色 | — | admin / engineer / viewer 本地桩 |
| 运行态 | 进程内状态、观测、日志 | `live`；采集关闭时为 `mock` |

连接测试的 HTTP 合同是 `POST /api/v1/devices/{id}/test`。Fake 恒成功。FOCAS 的完整握手在带 `Fwlib64.dll` 的 Host 进程里；这个接口只探测端口。

## 页面树（Studio IA）

1. **概览** `/` — 网关健康、revision、设备在线数、最近错误
2. **设备** `/devices` — 列表/新建/编辑；连接测试
3. **点位** `/points` — 表格式编辑；按设备过滤；CSV 导入导出（Excel 另存为 CSV）
4. **北向 MQTT** `/sinks/mqtt` — Broker、认证引用、Topic 模板、JSON 字段预览
5. **发布** `/publish` — 草稿 diff、校验结果、发布、回滚历史
6. **运行态** `/runtime` — 状态流、最近观测短窗口、日志尾
7. **系统** `/settings` — 站点名、许可证、用户角色（M1 可本地单用户+默认 admin）

各页调用的 API 与角色见 [studio-ia.md](studio-ia.md)。

## API（冻结草案）

见 [management-api.md](../api/management-api.md)。摘要：

- `GET` / `PUT` 配置资源、`POST` validate / publish / rollback
- `POST /devices/{id}/test`
- `GET` runtime/status、observations、logs/tail

嵌入 Host 时基路径为同源 `/api/v1`。

## 非功能

- .NET 10；SDK 仍由仓库根 `global.json` 固定（当前 10.0.203，`rollForward: disable`）
- 配置热加载优先，失败则安全重启采集循环（保留上一份已发布 revision，进程不退出）
- 中文优先 UI 文案；本文档中文为主
- 许可证 [Apache-2.0](../../LICENSE)；仓库不包含 FOCAS 厂商二进制
