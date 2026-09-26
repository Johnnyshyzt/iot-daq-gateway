# OPC UA

协议：OPC Foundation .NET Standard 客户端（`OPCFoundation.NetStandard.Opc.Ua.Client`）。会话使用无安全策略（SecurityPolicy None）。端点是 `opc.tcp://主机:端口/路径`。默认端口 4840。

## 控制器

| 适配器 | 型号 | 控制器侧条件 |
| --- | --- | --- |
| `siemens.opcua` | SINUMERIK 828D、840D sl、ONE（目录里的 808D / PCU50 也能选这个适配器，但没有单独的节点表） | 打开 OPC UA 选项并允许匿名或所填账号的无安全连接 |
| `heidenhain.opcua` | TNC 640、iTNC 等目录型号 | 控制器提供 OPC UA 服务器。没有公开的统一节点表 |
| `fagor.opcua` | 法格（型号表为空） | 同上 |
| `mitsubishi.opcua`、`citizen.opcua`、`dmg-mori.opcua`、`brother.opcua`、`generic.opcua` | 对应品牌或任意 OPC UA 服务器 | 在点位地址写 `ns=索引;s=…`，或让浏览名等于目录项 Id |

西门子内置路径（命名空间优先匹配 URI 中含 `sinumerik` 的索引，否则用索引 2，也可用连接参数里的命名空间 URI）：

- 状态 `/Channel/State/progStatus`（3 运行、2 暂停，报警号大于 0 为报警）
- 模式 `/Channel/State/opMode`（原样发布为 `mode:N`，不猜测模式名称）
- 程序、程序段、件数、进给、主轴、倍率、刀具、报警
- 轴位置 `/Channel/GeometricAxis/actToolBasePos[u1,1..3]`

用户名和密码是连接参数。证书目录在 `data/sdk/opcua-pki/`，不提交。

支持项见 Studio 品牌目录里该驱动的「驱动支持」列。西门子表以外的目录项是 `notAvailable`。非西门子驱动把能按 NodeId 或浏览名读取的项标为支持，并注明地址规则。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。西门子路径来自公开的 SINUMERIK 变量说明；进程内用假会话验证了映射，没有连过真实 840D。
