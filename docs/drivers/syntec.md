# 新代

适配器 `syntec.custom`。公开的 OpenCNC / SyntecRemoteCNC 函数签名没有核对到可以安全 P/Invoke 的版本，因此不发明入口。

把厂商文件放到 `data/sdk/syntec/`（或 `GATEWAY_SDK/syntec`）。缺文件时 API 和 Studio 显示 `SDK 未安装：请把 syntec 的厂商文件放到 <目录>`。文件在目录里时只报告已找到，不调用。

目录把状态、报警、程序、主轴、进给、坐标、件数、刀号、工作模式标为经由 SDK，其余为不可用。连接参数仍是主机、端口和超时，便于文件到齐后做网络探测。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。当前只验证了缺 SDK 时的状态。
