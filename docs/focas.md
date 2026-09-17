# FOCAS 说明

本仓库 **不包含** FANUC FOCAS 原生库、头文件或授权文件。

## 用户自行提供二进制

真实以太网 FOCAS 需要你从 FANUC 授权渠道取得并放到进程旁（或系统库路径）：

| 平台 | 典型文件名 |
| --- | --- |
| Windows | `Fwlib64.dll`（及厂商要求的依赖） |
| Linux | `libfwlib32.so` |

不要把这些文件提交进 git。`.gitignore` 已忽略常见名称。

## 代码中的位置

- 接口：`ISouthboundAdapter`（与 Fake 相同）
- 桩实现：`src/Adapters.Fanuc/Focas/FocasFanucAdapter.cs`
- P/Invoke TODO：`src/Adapters.Fanuc/Focas/FocasNative.cs`（`cnc_allclibhndl3` / `cnc_statinfo` / `cnc_rdalmmsg` / `cnc_rdprgnum`）
- 配置：`adapter: fanuc.focas`，见 `configs/examples/gateway.focas.yaml`

V1 不会加载或调用原生库；未实现时适配器保持 `offline`，避免在无库环境崩溃。

## 开发建议

日常开发使用 `fanuc.fake`。只有在已部署厂商库、并完成 `FocasNative` 绑定后，再切换 `fanuc.focas`。
