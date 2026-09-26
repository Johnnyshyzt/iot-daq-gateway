# 数控品牌目录

`cnc-data-template.xlsx` 是产品负责人提供的原始表。`cnc-catalog.json`（版本 `2026.09.26`）是由 `scripts/build-cnc-catalog.py` 生成的规范化目录，编译进 `Cnc.Catalog`，并在 Host 首次建库时写入数据库。

Sheet1 的每一列是一个品牌，单元格是该品牌能提供的中文数据项。Sheet2 是控制器型号。表头「凯恩第」和「凯恩帝」是同一品牌 `knd`。

## 标准项

同义词收成稳定的英文 Id，并带中文名、类型、单位和分类（状态、程序、主轴、进给、坐标、计数、时间、报警、刀具、温度、负载、系统信息）。

以下名称在同一品牌里经常同时出现，所以保持为两项，而不是并成一个：

- `state` 运行状态
- `workMode` 工作模式 / 操作模式

已经合并的例子：

- 主轴转速 / 实际主轴转速 / 实际主轴速度 → `spindleSpeed`（设定值是 `spindleSpeedCmd`）
- 机械坐标 / 机械位置 → `machinePosition`
- 报警信息 / 告警状态 / 是否报警 → `alarm`
- 加工件数 / 工件数 / 产量 → `partCount`

没有标准项可对应的列保留为品牌专有项，Id 形如 `fanuc_fanSpeed`。点位 Id 不能含点号，因为配置校验只允许字母、数字、下划线和连字符。

## 模板

- `fanuc-standard` 仍是历史三态模板（`state` / `alarm` / `program`），显示名「Fanuc 标准三态」。`fanuc.fake` 与 `fanuc.focas` 继续用它。
- `fanuc-catalog` 是发那科全表。其他品牌的内置模板是 `{brand}-standard`。
- 兄弟、哈斯、法格、台达、西铁城、森精机在 Sheet2 没有型号，创建设备时型号可留空。

`generic.ftp` 是 Sheet2「其他 = ftp/共享」的跨品牌适配器，不是第 20 个品牌。

重新生成：

```bash
python scripts/build-cnc-catalog.py
```
