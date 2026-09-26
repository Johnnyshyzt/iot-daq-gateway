# MTConnect

协议：HTTP GET `路径`，默认 `/current`，端口 5000。解析 MTConnect Streams / Devices XML，忽略命名空间。`/probe` 与 `/sample` 只要是同样的 DataItem 结构也能解析。

## 适配器

| 适配器 | 机床侧 |
| --- | --- |
| `mazak-smart.mtconnect` | Smooth / Smart 的 MTConnect 适配器 |
| `mazak-matrix.mtconnect` | Matrix 的 MTConnect 适配器 |
| `haas.mtconnect` | Haas NGC MTConnect |
| `dmg-mori.mtconnect` | DMG Mori MTConnect |
| `mitsubishi.mtconnect` | 三菱提供 MTConnect 时 |
| `brother.mtconnect` | 兄弟提供 MTConnect 时 |
| `generic.mtconnect` | 其它 Agent，包括 Okuma。Okuma 不是目录品牌 |

映射：`Execution` ACTIVE 为运行，INTERRUPTED / FEED_HOLD / STOPPED / READY 为空闲，UNAVAILABLE 为离线。`ControllerMode` 写入工作模式。急停 TRIGGERED 同时把状态写成报警。程序、注释、程序段、行号、件数、进给、倍率、主轴、刀号、按轴字母的位置，以及 Condition 的 Fault/Warning。

标准模板地址 `catalog/{itemId}` 不用改。Agent 的 DataItem 类型决定能否读到该项。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。XML 夹具已覆盖解析；没有连过现场 Agent。
