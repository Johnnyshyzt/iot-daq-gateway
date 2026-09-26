# 厂商 SDK 放置目录

网关不附带任何厂商二进制或头文件。把现场拿到的授权文件放到对应子目录，进程启动时按需加载。缺文件时驱动报告 `SDK 未安装`，不会让 Host 退出。

| 目录 | 驱动 | 需要的文件 |
| --- | --- | --- |
| `data/sdk/fanuc/` | `fanuc.focas` | Windows：`Fwlib64.dll`。Linux：`libfwlib32.so`。也可以放在 `Host.exe` 旁边。 |
| `data/sdk/mitsubishi/` | `mitsubishi.ezsocket` 及西铁城 / 森精机的 EZSocket 适配器 | 三菱 EZSocket 运行库。公开函数签名未核对，找到文件也不会调用。 |
| `data/sdk/syntec/` | `syntec.custom` | 新代 OpenCNC / SyntecRemoteCNC。签名未核对，不调用。 |
| `data/sdk/gsk/` | `gsk.custom` | 广州数控 SDK。签名未核对，不调用。 |
| `data/sdk/hnc/` | `hnc.custom` | 华中 HNC SDK。签名未核对，不调用。 |
| `data/sdk/knd/` | `knd.custom` | 凯恩帝 SDK。签名未核对，不调用。 |
| `data/sdk/baoyuan/` | `baoyuan.custom` | 宝元 SDK。签名未核对，不调用。 |
| `data/sdk/kede/` | `kede.custom` | 科德 SDK。签名未核对，不调用。 |
| `data/sdk/jdsoft/` | `jdsoft.custom` | 北京精雕 SDK。签名未核对，不调用。 |

可用环境变量 `GATEWAY_SDK` 指向另一个根目录。`HOST_DATA` 或 `STUDIO_DATA` 存在时，根目录是它们下面的 `sdk/`。

OPC UA 客户端证书写到 `data/sdk/opcua-pki/`（或 `GATEWAY_SDK/opcua-pki`）。那是运行时生成的，不要提交。
