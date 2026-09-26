# 发那科 FOCAS

适配器 `fanuc.focas`。以太网端口通常是 8193。控制器要打开 FOCAS Ethernet，并允许采集机 IP。

库文件不在仓库和安装包里。Windows x64 放 `Fwlib64.dll`，Linux 放 `libfwlib32.so`，位置是 `data/sdk/fanuc/` 或 `Host.exe` 同目录。也可以用 `GATEWAY_SDK/fanuc`。缺库时连接测试和健康状态写明未找到 `Fwlib64.dll` 以及应放置的目录，进程不退出。测试不会把 TCP 连通写成握手成功。

已绑定的调用（公开的 fwlib 布局，`Pack=4`）：

- `cnc_allclibhndl3` / `cnc_freelibhndl` / `cnc_settimeout`
- `cnc_statinfo`：状态、急停、工作模式、是否运行
- `cnc_rdalmmsg`：报警号
- `cnc_rdprgnum` / `cnc_rdprgnumo8`：当前程序与主程序
- `cnc_acts`：主轴实际转速
- `cnc_actf`：进给原始整数，不除以 1000（单位随系统而变）
- `cnc_absolute`：最多 8 轴，按 0.001 mm 换算（IS-B 假设）
- `cnc_sysinfo`：系统类型、版本、轴数

脚本化测试库只实现前三项，因此历史三态模板的测试仍是 3 个点。额外点只在原生库实现 `IFocasSignals` 时发布。

未绑定 `cnc_rdparam`：参数联合体的尺寸没有在本仓库钉死，不猜测布局。

更早的安装说明仍见 [../focas.md](../focas.md)。

验证状态：协议级或模拟器已核对，需在真实机床上做现场验收（verified on simulator/protocol level only, needs on-site acceptance on a real machine）。本环境没有 Fwlib，坐标换算必须在现场确认。
