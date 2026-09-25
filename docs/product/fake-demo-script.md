# Fake 演示脚本（约 10 分钟）

给销售或培训对着页面念。这条路径只证明 **Fake**：配置在页面里完成，采集读已发布 YAML，MQTT 能看到相位变化。不放置 `Fwlib64.dll`，不声称连过发那科机床。真机口径见 [pilot-acceptance.md](pilot-acceptance.md)。

## 开始前（1 分钟）

- [ ] Host 已启动，浏览器打开 `http://127.0.0.1:5080`
- [ ] 登录页是 **本机演示**：预填 `admin` / `admin`。若页面写「现场模式」，改用 `data\auth\bootstrap-password.txt` 并先改密，或换一台 `dotnet run` 的演示进程。现场包不要把 `admin` / `admin` 留在客户机器上
- [ ] 要给观众看报文时，Broker 已监听（常见 `127.0.0.1:1883`），另开一个订阅 `daq/#`。Broker 没起来时仍可演示到「发布」和运行态；跳过下面标了「MQTT」的两步，并说明采集还在、发布会被丢掉

## 跟着点（约 8 分钟）

- [ ] **0:00 登录。** `admin` / `admin`。说：这是 localhost 演示账号，现场 zip 会改成一次性引导密码。
- [ ] **0:30 设备。** 打开设备页，保留或新建一台 `fanuc.fake`（Id 用字母开头，例如 `cnc-01`）。连接测试应出现「Fake 适配器握手成功（未连接真实机床）」。说：测试成功只说明桩在，没有机床。
- [ ] **2:00 点位。** 打开该设备的点位，确认有 `state`、`alarm`、`program`。说：这是发那科适配器目录里的三个点，不是手填的 PLC 地址。可改启用、单位、倍率和死区。Excel 先另存 CSV 再导入，Id 必须在目录里。不要打开 YAML。
- [ ] **4:00 MQTT 页。** Broker 地址指向刚才那个订阅端，`clientId` 在这台机器上唯一。主题模板保持 `daq/{site}/{deviceId}/{point}`，状态主题保持 `daq/{site}/{deviceId}/$status`。需要账号时只填变量名 `MQTT_USER` / `MQTT_PASSWORD`，口令放在进程环境或现场的 `service.env`，不写进页面。无 Broker 则跳过订阅，仍把地址留成演示默认值。
- [ ] **6:00 校验、发布。** 打开发布页。先校验：有「错误」就不发布，按路径改。通过后发布。说：草稿在 `data/draft`，采集只读 `data/published`。同一进程马上重载，不用手写 YAML，也不用为此重启。
- [ ] **7:30 运行态。** `mode` 为 `live`，这台 Fake 设备 online。`state` 大约每 20 秒一档，在 `IDLE`、`RUNNING`、`ALARM` 之间转，`program` 为 `O0001`。说：这是时钟相位，不是机床状态。
- [ ] **8:30 MQTT。** 订阅里能看到 `daq/.../state` 随相位变化，以及每轮 `$status`。没有订阅就口头跳过，不要假装已经发出去。
- [ ] **9:30 回滚（可选）。** 在发布页回到上一份修订，运行态仍是 `live`。说：历史在 `data/revisions`，回滚同样不用手改文件。

## 收尾时要说的三句

- 配置的事实来源是发布后的 YAML。页面是编辑器，工程师不手写文件。
- 今天看到的在线是 `fanuc.fake`。FOCAS 代码在仓库里，厂商 `Fwlib64.dll` 不在包里，本仓库没有真机采集记录。
- 客户机床上的第一次联调另走 [pilot-acceptance.md](pilot-acceptance.md)，不把这次演示当成验收。
