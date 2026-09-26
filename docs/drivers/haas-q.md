# 哈斯 Q 指令

协议：TCP 端口 5051。查询 `?Qnnn` 加换行，控制以 `>` 结束应答。机床 Setting 143（Machine Data Collect）必须打开。

默认查询：Q100 序列号、Q101 版本、Q102 型号、Q104 模式、Q201 刀号、Q300 上电时间、Q301 运行时间、Q303 循环时间、Q402 件数、Q403 总件数、Q500 程序/状态/件数。时间 `HHH:MM:SS` 换成秒。Q600 宏变量不是默认查询。

`haas.mtconnect` 是另一条路径，见 [mtconnect.md](mtconnect.md)。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。应答解析和进程内提示符服务器已测；没有连过 NGC。
