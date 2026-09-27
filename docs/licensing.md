# 授权

许可证是离线 JSON 文件，算法 ES256（ECDSA P-256）。Host 里嵌着公钥（`ProductKeys`）。私钥用 `tools/license-gen` 从路径读取，不要提交。

```bash
export LICENSE_PRIVATE_KEY=/secure/license-private.pem
dotnet run --project tools/license-gen -- \
  --customer "示例工厂" \
  --edition commercial \
  --devices 100 \
  --feature opcua --feature http-push --feature alarm-notifications \
  --feature scheduled-reports --feature query-api \
  --feature rules --feature oee \
  --expires 2027-12-31T00:00:00Z \
  --out customer.license.json
```

省略 `--devices`、`--points` 或 `--expires` 表示这一项不限制。`--fingerprint` 可选，值来自 Studio「授权许可」页。当前指纹是 `fp2`：系统标识、主机名、主板信息和网卡的哈希。系统标识必须一致；主机名和主板信息最多差一项；已记录网卡时允许更换一块。旧的 32 位十六进制指纹仍按系统标识加主机名比对。

时钟回拨和状态被改时，采集继续，配置修改被拒绝，页面顶部出提示。上次见到的时间写在数据库和 `data/security/clock.json`，用 `data/security/state.key` 做 HMAC。`Licensing:ClockDriftToleranceSeconds` 默认 300。这不是 TPM。同时删掉密钥和两处记录会重新起算。

没有许可证时是社区版。默认上限和功能清单在 `appsettings.json` 的 `Licensing` 节，现场包的 `appsettings.Field.json` 同样有一份。这是可改的默认值，不是价格。

默认拦截的功能是 `opcua`、`http-push`、`alarm-notifications`、`scheduled-reports`、`query-api`、`rules`、`oee`。计算点（含差值、速率、移动平均、持续为真、计数回绕和距上次变化）不在清单里，社区版可以使用。`rules` 是边缘规则的保存、回测和采集时执行。`oee` 是 OEE 报表、停机原因、班次休息与节假日、计划停机、理想节拍、状态映射和报废登记。未授权时 `GET /api/v1/oee` 仍返回 200，正文是 `licensed: false` 和公式，不返回报表。

开发环境的 `appsettings.Development.json` 给社区版功能 `*`，方便本机调试。生产配置不要带这个标记。

测试用的密钥在测试进程里临时生成，不写入仓库。换产品公钥时，只替换 `ProductKeys.SpkiBase64`，私钥留在签发机。
