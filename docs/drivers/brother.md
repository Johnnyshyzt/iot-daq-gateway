# 兄弟

`brother.custom` 不实现 CNC-C00。公开资料里没有可以对照的帧格式，驱动不会编造握手。连接测试返回固定说明，并指向 `brother.mtconnect` 与 `brother.opcua`。

型号表为空。有 MTConnect Agent 或 OPC UA 服务器时用对应适配器，见 [mtconnect.md](mtconnect.md) 与 [opcua.md](opcua.md)。

目录支持级别：专有适配器的全部数据项为不可用。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。这里的核对是“确认没有可实现的公开帧”，不是现场通信。
