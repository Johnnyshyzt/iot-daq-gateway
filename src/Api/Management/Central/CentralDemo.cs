using IotDaq.Persistence;
using IotDaq.Persistence.Shop;

namespace Studio.Host.Central;

public static class CentralDemo
{
    public static void Seed(GatewayPersistence database)
    {
        if (database.GetSetting("demo.central") == "1")
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        database.SaveFleetGroup(new FleetGroupRow { Id = "shop-1", Name = "一车间" });
        database.SaveFleetGroup(new FleetGroupRow { Id = "shop-2", Name = "二车间" });
        SeedGateway("edge-1", "边缘 1 · 一车间", "shop-1", "一车间", "online", now - 5_000, 6, 5, 1);
        SeedGateway("edge-2", "边缘 2 · 一车间", "shop-1", "一车间", "online", now - 8_000, 4, 4, 0);
        SeedGateway("edge-3", "边缘 3 · 二车间", "shop-2", "二车间", "offline", now - 86_400_000, 3, 0, 3);
        database.AddCentralAudits(
        [
            new CentralAuditRow
            {
                GatewayId = "edge-3",
                Username = "central",
                Role = "system",
                Action = "central.offline",
                Target = "edge-3",
                Detail = "网关 边缘 3 · 二车间 已离线",
                UnixMs = now - 3_600_000
            }
        ]);
        database.ScanOfflineGateways(now, 60_000);

        var v1 = """
            {
              "managed": ["rules"],
              "local": ["connection.host", "connection.port", "connection.password", "displayName"],
              "rules": [
                { "id": "central-load", "name": "主轴负载", "expression": "spindleLoad > 90", "enabled": true, "durationMs": 0, "actionsJson": "[]" }
              ]
            }
            """;
        var v2 = """
            {
              "managed": ["rules"],
              "local": ["connection.host", "connection.port", "connection.password", "displayName"],
              "rules": [
                { "id": "central-load", "name": "主轴负载", "expression": "spindleLoad > 80", "enabled": true, "durationMs": 5000, "actionsJson": "[]" }
              ]
            }
            """;
        database.AddTemplateVersion(new CentralTemplateRow
        {
            Key = "spindle",
            Name = "主轴规则",
            Kind = "rules",
            BodyJson = v1.Trim(),
            Comment = "第一版",
            CreatedBy = "admin",
            CreatedUnixMs = now - 7_200_000
        });
        database.AddTemplateVersion(new CentralTemplateRow
        {
            Key = "spindle",
            Name = "主轴规则",
            Kind = "rules",
            BodyJson = v2.Trim(),
            Comment = "阈值改为 80，持续 5 秒",
            CreatedBy = "admin",
            CreatedUnixMs = now - 3_600_000
        });
        database.AddConfigPush(new ConfigPushRow
        {
            Id = "demo-push",
            TemplateKey = "spindle",
            Version = 2,
            PreviousVersion = 1,
            GroupId = "shop-1",
            DiffText = TextDiff.Unified(v1.Trim(), v2.Trim()),
            ConflictPolicy = "central-wins",
            Status = "applying",
            CreatedBy = "admin",
            CreatedUnixMs = now - 1_800_000
        }, ["edge-1", "edge-2"]);

        var rolloutDir = Path.Combine(Path.GetTempPath(), "daq-demo-rollout");
        Directory.CreateDirectory(rolloutDir);
        database.AddRollout(new RolloutRow
        {
            Id = "demo-rollout",
            Version = "0.11.0",
            Sha256 = "demo",
            PackagePath = Path.Combine(rolloutDir, "demo.zip"),
            Status = "rolling",
            CreatedBy = "admin",
            CreatedUnixMs = now - 600_000
        }, ["edge-1", "edge-2", "edge-3"]);
        database.SetRolloutTarget("demo-rollout", "edge-1", "staged", "已校验并暂存。等待重启。", now - 500_000);
        database.SetRolloutTarget("demo-rollout", "edge-2", "pending", "等待边缘心跳领取。", now - 500_000);
        database.SetRolloutTarget("demo-rollout", "edge-3", "failed", "网关离线，未能下载升级包。", now - 400_000);
        database.SetSetting("demo.central", "1");

        void SeedGateway(string id, string name, string group, string site, string status, long seen, int devices, int online, int offline)
        {
            database.UpsertFleetGateway(new FleetGatewayRow
            {
                Id = id,
                Name = name,
                GroupId = group,
                Site = site,
                Version = "0.11.0",
                LicenseSummary = "commercial/valid",
                DeviceCount = devices,
                OnlineLinks = online,
                OfflineLinks = offline,
                WorkingSetMb = 180,
                SummaryJson = "{\"devices\":[{\"id\":\"demo-fanuc\",\"name\":\"演示数据 · 发那科\",\"state\":\"RUNNING\",\"program\":\"O0001\",\"alarm\":\"0\"}]}",
                Status = status,
                LastSeenUnixMs = seen,
                EnrolledUnixMs = now - 86_400_000
            });
        }
    }
}
