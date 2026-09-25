# 商业一页（边界，不含价格）

Open Core 的产品边界见 [open-core.md](open-core.md)。没有自有发那科机床时能承诺什么、不能写什么，见 [pilot-acceptance.md](pilot-acceptance.md)。

相关文档：可贴进合同的范围附件 [pilot-contract-appendix.md](pilot-contract-appendix.md)，给买家的 [sales-one-pager.md](sales-one-pager.md)，安全口径 [security-narrative.md](security-narrative.md)。本文仍只留价格空位。

**价格数字由 Johnny 填写。** 下表只留空位，仓库里不写人民币或其他金额。

## 开源、随仓库交付

Apache-2.0，在本仓库里：

- 现场一个 Host：采集、管理 API、Studio 页面
- Fake 全路径：设备、点位、MQTT、校验、发布、回滚、运行态
- `fanuc.focas` 适配器源码。不随仓库、镜像或 zip 分发 `Fwlib64.dll`；缺库时进程继续跑，设备测试给出中文失败

## 商业层（叙事，价格待填）

| 项 | 卖什么 | 价格 |
| --- | --- | --- |
| 首台 FOCAS 联调 | 客户提供机床、网络和 64 位 `Fwlib64.dll` 之后的实施人天。出发前 Fake 清单不过，不上门。见试点验收 | TBD（Johnny） |
| 年维保 / 支持 | 已安装网关的升级协助、故障通讯、版本说明。不含新品牌、不含 OPC UA | TBD（Johnny） |
| 现场实施 | 安装目录、`service.env`、本机账号、网络分段。按次或按人天 | TBD（Johnny） |

许可证闸门当前不拦截请求。真正的许可证服务不在 M1。

## 以后的付费路线（占位，M1 不做）

| 项 | 状态 | 价格 |
| --- | --- | --- |
| Fleet / 远程管理多台网关 | 未做 | TBD（Johnny） |
| 发那科以外的品牌 | 未做 | TBD（Johnny） |
| OPC UA 北向 | 未做 | TBD（Johnny） |

## 计价轴（二选一或组合，先空着）

还没选定用哪一根轴报价。两列都是空位，不是报价。

| 轴 | 含义 | 单价 |
| --- | --- | --- |
| 按网关现场 | 一台采集机 / 一个 Host 为一档，不管下面挂几台 CNC | TBD（Johnny） |
| 按 CNC 台数 | 按已启用的机床数量分档 | TBD（Johnny） |

选定之前，合同上写「计价轴待定」，不要填临时数字。
