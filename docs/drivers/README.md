# 协议驱动

运行配置只在数据库里。Studio 按适配器参数生成连接表单，`POST /api/v1/devices/test` 用未保存的参数做一次连接测试，`POST /api/v1/devices/{id}/test` 测试已保存设备。`GET /api/v1/catalog/support?brandId=` 给出每个目录项的支持级别：`supported`、`viaSdk`、`notAvailable`。

模拟器（`*.sim`、`fanuc.fake`）仍然是演示路径，不连接机床。

| 驱动 | 文档 |
| --- | --- |
| OPC UA | [opcua.md](opcua.md) |
| MTConnect | [mtconnect.md](mtconnect.md) |
| 海德汉 LSV2 | [lsv2.md](lsv2.md) |
| 哈斯 Q 指令 | [haas-q.md](haas-q.md) |
| Modbus TCP / 台达 | [modbus.md](modbus.md)、[delta.md](delta.md) |
| FTP | [ftp.md](ftp.md) |
| 发那科 FOCAS | [focas.md](focas.md) |
| 兄弟 | [brother.md](brother.md) |
| 三菱 EZSocket | [mitsubishi.md](mitsubishi.md) |
| 新代 | [syntec.md](syntec.md) |
| 广州数控 | [gsk.md](gsk.md) |
| 华中 | [hnc.md](hnc.md) |
| 凯恩帝 | [knd.md](knd.md) |
| 宝元 | [baoyuan.md](baoyuan.md) |
| 科德 | [kede.md](kede.md) |
| 精雕 | [jdsoft.md](jdsoft.md) |

验证状态一律是：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。
