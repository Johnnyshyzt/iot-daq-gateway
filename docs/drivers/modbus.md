# Modbus TCP

协议：功能码 03（保持寄存器）和 04（输入寄存器）。默认端口 502，从站号 1。适配器：`generic.modbus`、`delta.modbus`、`delta.custom`。

这是网关自己的默认表，不是台达或任何 PLC 的出厂地址。现场地址写到点位模板：`modbus:holding:N` 或 `modbus:input:N`。`catalog/{itemId}` 继续用默认表。

| 寄存器 | 目录项 | 编码 |
| --- | --- | --- |
| 0 | state | 0 空闲，1 运行，2 报警 |
| 1 | alarm | 数值 |
| 2 | program | 程序号，发布为 `Oxxxx` |
| 3 | partCount | 整数 |
| 4 / 5 | spindleSpeed / spindleSpeedCmd | 整数 |
| 6 | spindleOverride | 整数 |
| 7 / 8 | feedRate / feedRateCmd | 整数 |
| 9 | feedOverride | 整数 |
| 10 / 11 / 12 | X / Y / Z | 整数 |
| 13 | toolNumber | 整数 |
| 14 | workMode | 0 AUTO，1 MDI，2 JOG，3 EDIT |
| 15 | partCountTotal | 整数 |

不需要厂商 SDK。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。功能码 03 已用进程内服务器验证。
