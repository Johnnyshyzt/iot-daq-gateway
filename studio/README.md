# Config Studio（M1 脚手架）

采集网关的可视化配置台。M1 和开源网关放在同一个仓库里。`Studio.Host` 现在单独跑管理 API 和页面；`MapStudioApi()` 留给以后嵌进网关进程。设备只做 Fanuc（`fanuc.fake` / `fanuc.focas`），北向只做 MQTT。

许可证与仓库根目录相同：[Apache-2.0](../LICENSE)。产品边界见 [docs/product/open-core.md](../docs/product/open-core.md)，配置树见 [docs/config/README.md](../docs/config/README.md)，页面树见 [docs/product/studio-ia.md](../docs/product/studio-ia.md)。

## 配置怎么存

YAML 文件是唯一真相，不靠数据库。目录在 `studio/data`（可用环境变量 `STUDIO_DATA` 改掉）：

```
studio/data/
  seed/                 首次启动时复制的示例（进 git）
  published/            已发布配置，含 .revision；Gateway.Host 读这里
  draft/                正在编辑的草稿
  revisions/{sha256}/   发布快照，供回滚
  runtime/studio.log    Studio 自己的日志
```

`published/`、`draft/`、`revisions/`、`runtime/` 是本机状态，不进 git。更早的 `studio/data/config/` 会在下次启动时复制到 `published/`。修订号是 `CanonicalRevision`：整包规范化 JSON 的 SHA-256（见配置说明）。发布时把草稿写成已发布配置并留下快照；回滚会同时覆盖已发布配置和草稿。发布和回滚之后，Studio 会请求网关 `POST /api/v1/runtime/reload`。

MQTT 密码只写环境变量名（`passwordFromEnv`），不把明文密码写进 YAML。示例见 `studio/data/seed/secrets.env.example`。

## 项目

```
studio/Studio.sln
studio/src/Studio.Contracts/   文档和 API 模型
studio/src/Studio.Host/       ASP.NET Core 管理 API，可选用静态页托管前端
studio/web/                   React + Vite + shadcn-admin 壳
studio/tests/Studio.Tests/    草稿、发布、回滚
```

`MapStudioApi()` 是以后嵌入 `Gateway.Host` 的接缝。当前是两个进程：Studio 写 `published/`，网关加载该目录，并通过 `127.0.0.1:5081` 把运行态交回 Studio。

## 运行 API

需要与仓库相同的 .NET SDK **10.0.203**（根目录 `global.json`）。

```bash
dotnet build studio/Studio.sln
dotnet test studio/Studio.sln
dotnet run --project studio/src/Studio.Host
```

默认监听 `http://127.0.0.1:5080`。第一次启动会把 `studio/data/seed` 复制成已发布配置和草稿。

本地桩账号（不要用于生产）：

| 用户 | 密码 | 角色 |
| --- | --- | --- |
| admin | admin | 管理员，可改草稿、发布，并看到用户列表 |
| engineer | engineer | 可改草稿、校验、发布、回滚、连接测试 |
| viewer | viewer | 只读 |

没有令牌时，`/api/v1/*` 返回 401。`POST /api/v1/auth/login` 换 Bearer 令牌。签名密钥用 `Studio:SigningKey` 或 `STUDIO_SIGNING_KEY`；留空时使用仅限开发的回退密钥。

## 运行页面

前端是 React + Vite，界面壳来自 [shadcn-admin](https://github.com/satnaing/shadcn-admin)（MIT，见 [studio/web/README.md](web/README.md)）。导航和表单是中文。CI 用 npm；本机也可以用 pnpm。

开发时前端单独起，把 `/api` 代理到 5080：

```bash
cd studio/web
npm install
npm run dev
```

打开 `http://127.0.0.1:5173`，用 `admin` / `admin` 登录。

生产构建（产物在 `studio/web/dist`，由 `Studio.Host` 挂到同一端口）：

```bash
cd studio/web
npm install
npm run build
dotnet run --project studio/src/Studio.Host
```

然后打开 `http://127.0.0.1:5080`。`npm run build` 之后如果 Host 已经在跑，需要重启一次才能看到新页面。

页面：概览、设备、点位、北向 MQTT、发布、运行态、系统。点位支持 CSV 导入导出；Excel 请另存为 CSV。

## 和 Gateway.Host 一起跑

需要本机 MQTT broker 才能在 Studio 里看到真实报文。没有 broker 时网关仍会采集，只是 MQTT 发布被丢掉，运行态里的观测仍然来自适配器。

```bash
# 终端 1：broker
mosquitto -c docker/mosquitto.conf

# 终端 2：先启动 Studio，生成 studio/data/published
dotnet run --project studio/src/Studio.Host

# 终端 3：网关读取 Studio 已发布目录
dotnet run --project src/Gateway.Host --no-launch-profile -- --config studio/data/published

# 另开终端看 MQTT
mosquitto_sub -h 127.0.0.1 -t 'daq/#' -v
```

然后打开 `http://127.0.0.1:5080`，用 `admin` / `admin` 登录。改设备或点位，打开发布页发布。Studio 会通知 `http://127.0.0.1:5081/api/v1/runtime/reload`（可用 `Studio:GatewayLoopback` 或网关侧 `GATEWAY_LOOPBACK` 修改；设为 `off` 则网关不监听）。网关也监视已发布目录，作为这次通知的备份。重载失败时上一份会话继续跑。

运行态页在网关可达时显示「来自本机 Gateway.Host」（`mode` 为 `live`）。只开 Studio 时仍是模拟数据（`mode` 为 `mock`）：Fake 设备显示在线，观测值按时间相位变化。FOCAS 的「连接测试」在 Studio 里只探测 TCP 端口，完整握手仍在带 `Fwlib64.dll` 的网关进程中。

不想开 Studio 时，网关仍可直接加载示例包：

```bash
dotnet run --project src/Gateway.Host --no-launch-profile -- --config configs/examples/v1
```

单文件快速开始 `configs/examples/gateway.yaml` 保持不变，见仓库根 [README](../README.md)。

## API

基路径 `/api/v1`。写操作需要 `admin` 或 `engineer`。错误形如 `{ "code", "message", "details?" }`。

| 方法 | 路径 | 作用 |
| --- | --- | --- |
| POST | `/auth/login` | 登录，返回 Bearer 令牌 |
| GET | `/auth/me` | 当前用户 |
| GET | `/config` | 草稿、已发布配置、修订 |
| GET | `/config/diff` | 草稿相对已发布的差异 |
| GET/PUT/DELETE | `/config/devices/{id}` | 设备草稿 |
| GET | `/config/devices` | 设备列表 |
| GET/PUT | `/config/points/{deviceId}` | 点位草稿 |
| GET/PUT | `/config/sinks/mqtt` | MQTT 草稿 |
| GET/PUT | `/config/gateway` | 网关站点草稿 |
| POST | `/config/validate` | 校验草稿 |
| POST | `/config/publish` | 发布，返回 `revision` |
| GET | `/config/revisions` | 最近修订 |
| POST | `/config/rollback` | `{ "revision": "<sha256>" }` |
| POST | `/devices/{id}/test` | 连接测试 |
| GET | `/runtime/status` | 网关在跑时为真实状态，否则模拟 |
| GET | `/runtime/observations` | `deviceId`、`limit` |
| GET | `/runtime/logs/tail` | `lines` |
| GET | `/settings` | 站点、许可证桩、用户 |
| PUT | `/settings` | 把站点字段写入草稿 |
| GET | `/license` | 许可证桩，当前不拦截 API |

下面这条会走完「新建设备 → 点位 → MQTT → 发布 → 回滚」。先登录拿到令牌：

```bash
TOKEN=$(curl -s http://127.0.0.1:5080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"engineer","password":"engineer"}' | jq -r .token)

curl -s -X PUT http://127.0.0.1:5080/api/v1/config/devices/cnc-02 \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"metadata":{"id":"cnc-02","displayName":"铣床 02"},"spec":{"adapter":"fanuc.fake","enabled":true,"intervalMs":1000,"connection":{"host":"10.0.0.8","port":8193}}}'

curl -s -X POST http://127.0.0.1:5080/api/v1/config/publish \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"note":"add cnc-02"}'
```

## 许可证桩

`GET /api/v1/license` 固定返回未强制校验。没有许可证文件时，Runtime 与 Studio 都可以运行，管理 API 也不拦截。
