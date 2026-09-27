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
  --expires 2027-12-31T00:00:00Z \
  --out customer.license.json
```

省略 `--devices`、`--points` 或 `--expires` 表示这一项不限制。`--fingerprint` 可选，值来自 Studio「授权许可」页。

没有许可证时是社区版。默认上限和功能清单在 `appsettings.json` 的 `Licensing` 节，现场包的 `appsettings.Field.json` 同样有一份。这是可改的默认值，不是价格。

开发环境的 `appsettings.Development.json` 给社区版功能 `*`，方便本机调试。生产配置不要带这个标记。

测试用的密钥在测试进程里临时生成，不写入仓库。换产品公钥时，只替换 `ProductKeys.SpkiBase64`，私钥留在签发机。
