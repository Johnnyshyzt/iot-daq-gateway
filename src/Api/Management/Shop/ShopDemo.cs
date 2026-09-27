using IotDaq.Persistence;
using IotDaq.Persistence.Shop;

namespace Studio.Host.Shop;

public static class ShopDemo
{
    public static void Seed(GatewayPersistence database)
    {
        if (database.GetSetting("demo.tools") == "1")
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        database.SaveTool(Tool("T01", "面铣刀", 1000, null, 80));
        database.SaveTool(Tool("T02", "钻头", 500, null, 80));
        database.SaveTool(Tool("T03", "丝锥", null, 60, 80));
        database.SavePocket(new ToolPocketRow { Id = "demo-pocket-1", DeviceId = "demo-fanuc", Pocket = 1, ToolNumber = "T01" });
        database.SavePocket(new ToolPocketRow { Id = "demo-pocket-2", DeviceId = "demo-fanuc", Pocket = 2, ToolNumber = "T02" });
        database.AdvanceTool("demo-fanuc", new ToolSignal("T01", 0, true, null, now - 10_000, "points"));
        database.AdvanceTool("demo-fanuc", new ToolSignal("T01", 850, true, null, now - 5_000, "points"));
        database.AdvanceTool("demo-haas", new ToolSignal("T02", 0, false, null, now - 8_000, "points"));
        database.AdvanceTool("demo-haas", new ToolSignal("T02", 500, false, null, now - 4_000, "points"));
        database.RecordToolChange(new ToolChangeRow
        {
            Id = "demo-change-1",
            DeviceId = "demo-fanuc",
            Pocket = 1,
            OldToolNumber = "T03",
            NewToolNumber = "T01",
            Note = "演示换刀，不重置已累计的预警寿命",
            Actor = "operator",
            UnixMs = now - 3_000
        }, resetLife: false);

        var programId = "demo-o0001";
        database.SaveProgram(new NcProgramRow
        {
            Id = programId,
            Name = "O0001",
            Comment = "演示程序",
            Status = "approved",
            CreatedBy = "engineer",
            UpdatedUnixMs = now
        });
        var first = "O0001\nG90 G54\nM03 S1200\nG01 X10 F200\nM30\n";
        var second = "O0001\nG90 G54\nM03 S1500\nG01 X12 F180\nM08\nM30\n";
        database.AddProgramVersion(new NcProgramVersionRow
        {
            Id = "demo-o0001-v1",
            ProgramId = programId,
            Content = first,
            Checksum = ContentHash.Sha256(first),
            Comment = "初稿",
            Status = "archived",
            UploadedBy = "engineer",
            UploadedUnixMs = now - 86_400_000
        });
        database.AddProgramVersion(new NcProgramVersionRow
        {
            Id = "demo-o0001-v2",
            ProgramId = programId,
            Content = second,
            Checksum = ContentHash.Sha256(second),
            Comment = "提高转速并打开冷却",
            Status = "approved",
            UploadedBy = "engineer",
            UploadedUnixMs = now - 3_600_000,
            ApprovedBy = "admin",
            ApprovedUnixMs = now - 3_000_000
        });
        database.SetProgramDevices(programId, ["demo-fanuc", "demo-siemens"]);
        database.SetSetting("demo.tools", "1");
    }

    private static ToolRow Tool(string number, string description, double? count, double? minutes, double warning) => new()
    {
        Id = "tool-" + number.ToLowerInvariant(),
        ToolNumber = number,
        Description = description,
        LifeLimitCount = count,
        LifeLimitMinutes = minutes,
        WarningPercent = warning,
        Enabled = true,
        UpdatedBy = "demo",
        UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
}
