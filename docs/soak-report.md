# 采集浸泡报告 / Soak report

实测，不是估算。命令：

```bash
dotnet run --project tools/soak/Soak.csproj -c Release -- --devices 200 --minutes 40 --interval-ms 2000 --out /tmp/soak-report.json
```

路径是发那科模拟器 `BrandSimulatorAdapter.Sample` 再经 `GatewayPersistence.Write` 写入 SQLite。不启动完整 LiveGateway，不连接 MQTT。请求的点位 Id 是 `state`、`alarm`、`alarmNumber`、`program`、`partCount`、`spindleSpeed`、`spindleLoad`、`feedRate`。模拟器只发出该品牌目录里存在的点，观测条数对应每台 7 个点。

机器是这次云代理虚拟机，4 个逻辑处理器。开始 `2026-09-27T06:29:20Z`，结束 `2026-09-27T07:09:21Z`。

| 项 | 值 |
| --- | --- |
| 设备 | 200 |
| 周期 | 2000 ms |
| 时长 | 2400.9 s |
| 扫描轮数 | 1200 |
| 观测条数 | 1,680,000（1200 × 200 × 7） |
| 批延迟均值 / p50 / p95 / 最大 | 225.03 / 224.50 / 304.98 / 608.24 ms |
| CPU | 进程 CPU 时间 / 墙钟 / 4 = 0.0139（约 0.056 个核） |
| 工作集 首 / 最大 / 末次采样 | 143.0 / 230.2 / 219.4 MB |
| 托管堆 首 / 最大 / 末次采样 / 强制 GC 后 | 62.2 / 90.3 / 45.7 / 4.9 MB |
| 线程 首 / 最大 / 末 | 13 / 14 / 12 |
| GC gen0 / gen1 / gen2 | 605 / 528 / 172 |
| `gateway.db` | 349,421,568 bytes |

每 30 秒采样 80 次。工作集在预热后停在大约 220–230 MB，没有随轮数继续爬升。托管堆在几十 MB 内来回，强制 GC 之后回到 4.9 MB。线程数停在 12–14。批延迟的 p95 是 305 ms，最大一次 608 ms，都低于 2000 ms 的周期，所以扫描没有越积越慢。

`gateway.db` 约 333 MiB，因为历史表按每次采样追加，保留任务按小时删，40 分钟内不会裁掉这些行。这是预期的库增长，不是托管堆泄漏。这次没有改采集或入库代码。

JSON 里 `workingSetMb.afterGc` 写的是强制 GC 之后的托管堆字节，不是工作集。上表的工作集只用首、最大和末次采样。
