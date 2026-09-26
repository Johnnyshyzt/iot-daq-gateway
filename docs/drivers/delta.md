# 台达

真实路径是 Modbus TCP，见 [modbus.md](modbus.md)。`delta.modbus` 与 `delta.custom` 是同一个驱动。

没有单独的台达 SDK 调用。目录里的暂停时间、刀库刀号等专有项没有默认寄存器，需要在点位地址写 `modbus:holding:N` 或 `modbus:input:N`。

端口 502。PLC 侧要打开 Modbus TCP 服务器，从站号与表单一致。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。
