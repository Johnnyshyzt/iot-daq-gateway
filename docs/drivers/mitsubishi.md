# 三菱

开放协议：

- `mitsubishi.opcua`：见 [opcua.md](opcua.md)。没有内置 M70/M80/M800 节点表，用 NodeId 或浏览名。
- `mitsubishi.mtconnect`：见 [mtconnect.md](mtconnect.md)。

EZSocket：`mitsubishi.ezsocket`、`mitsubishi.custom`，以及西铁城 `citizen.mitsubishi`、森精机 `dmg-mori.mitsubishi`。加载目录 `data/sdk/mitsubishi/`。

EZSocket 的公开头文件和稳定入口没有核对到可引用的签名。目录里声明状态、报警、程序、主轴、进给、坐标、件数、刀号、工作模式为「经由 SDK」。文件缺失时报告 `SDK 未安装：请把 mitsubishi 的厂商文件放到 …`。文件存在时只列出文件名，不调用未知入口。

不要把厂商 DLL 提交进仓库。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。SDK 路径只验证了缺文件时的状态，没有调用过 EZSocket。
