# 已并入 Host

配置台不再单独启动。三个模块都在仓库 `src/` 里，由一个进程提供页面、管理 API 和采集：

```bash
cd src/Web && npm ci && npm run build
dotnet run --project src/Host
```

浏览器打开 `http://127.0.0.1:5080`。说明见仓库根目录 [README](../README.md)。
