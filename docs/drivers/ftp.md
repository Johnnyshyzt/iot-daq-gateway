# FTP 状态文件

适配器 `generic.ftp`。端口 21。登录后 `TYPE I`、`PASV`、`RETR`。默认路径 `/status.txt`，用户 `anonymous`。

文件每行 `键=值` 或 `键,值`。键使用目录项 Id 时写入对应点。标准模板不用改地址。

这是给已经把状态落到文件或共享目录转 FTP 的控制器用的，不是机床私有协议。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。解析和进程内 FTP 服务器已测。
