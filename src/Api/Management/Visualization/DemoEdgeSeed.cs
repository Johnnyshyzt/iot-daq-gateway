using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using IotDaq.Persistence.Visualization;
using Studio.Contracts;

namespace Studio.Host.Visualization;

/// <summary>
/// Gives demo devices stop reasons, a shift calendar, ideal cycle times, and one computed point
/// so the OEE and rules pages are not empty. It does not invent vendor data.
/// </summary>
public static class DemoEdgeSeed
{
    public static void Apply(GatewayPersistence database, ConfigBundle bundle)
    {
        if (string.Equals(database.GetSetting("demoEdgeSeeded"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var devices = bundle.Devices.Where(device => device.Metadata.Id.StartsWith("demo-", StringComparison.Ordinal)).ToList();
        if (devices.Count == 0)
        {
            return;
        }

        var calendar = ShiftCalendar.ParseOrDefault(database.GetSetting("shiftCalendar"));
        if (calendar.Breaks.Count == 0)
        {
            calendar.Breaks =
            [
                new PlannedBreak { Name = "午饭", Start = "12:00", End = "13:00" },
                new PlannedBreak { Name = "夜宵", Start = "02:00", End = "03:00" }
            ];
            calendar.Holidays = ["2026-10-01"];
            database.SetSetting("shiftCalendar", calendar.ToJson());
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var device in devices)
        {
            database.SaveCycleTime(new CycleTimeRow
            {
                Id = "cycle-" + device.Metadata.Id,
                Scope = "device",
                OwnerId = device.Metadata.Id,
                Program = "",
                IdealSeconds = 48
            });
            database.AddScrap(new ScrapEntryRow
            {
                Id = "scrap-" + device.Metadata.Id,
                DeviceId = device.Metadata.Id,
                UnixMs = now - 3 * 60 * 60 * 1000,
                Quantity = device.Metadata.Id.EndsWith("fanuc", StringComparison.Ordinal) ? 2 : 1,
                Note = "演示报废",
                EnteredBy = "demo",
                Source = "manual"
            });
        }

        database.SavePlannedStop(new PlannedStopRow
        {
            Id = "plan-demo-fanuc",
            Scope = "device",
            OwnerId = "demo-fanuc",
            Name = "午间保养",
            StartUnixMs = now - 5 * 60 * 60 * 1000,
            EndUnixMs = now - 5 * 60 * 60 * 1000 + 40 * 60 * 1000
        });

        using (var db = database.CreateContext())
        {
            var transitions = db.StateTransitions.Where(row => row.DeviceId.StartsWith("demo-")).OrderBy(row => row.StartedUnixMs).ToList();
            var idleIndex = 0;
            foreach (var row in transitions)
            {
                if (!string.Equals(row.State, MachineState.Idle, StringComparison.Ordinal))
                {
                    continue;
                }

                idleIndex++;
                row.State = (idleIndex % 9) switch
                {
                    0 => MachineState.Planned,
                    1 or 2 => MachineState.Setup,
                    3 => MachineState.Waiting,
                    _ => MachineState.Idle
                };
            }

            db.SaveChanges();
            var events = db.DowntimeEvents.Where(row => row.DeviceId.StartsWith("demo-")).OrderBy(row => row.StartedUnixMs).ToList();
            var cursor = 0;
            foreach (var item in events)
            {
                var state = transitions.FirstOrDefault(row => row.Id == item.TransitionId)?.State ?? item.State;
                item.State = state;
                item.ReasonId = ReasonFor(state, cursor++);
                item.Source = "demo";
                item.AssignedBy = "demo";
                item.AssignedUnixMs = item.StartedUnixMs;
            }

            db.SaveChanges();
        }

        database.SaveComputedPoint(new ComputedPointRow
        {
            Id = "calc-demo-load",
            Scope = "device",
            OwnerId = "demo-fanuc",
            PointId = "calc_loadHigh",
            Name = "主轴高负载",
            Unit = "",
            Expression = "if(spindleLoad > 80, 1, 0)",
            Enabled = true,
            UpdatedUnixMs = now,
            UpdatedBy = "demo"
        });
        database.SaveEdgeRule(new EdgeRuleRow
        {
            Id = "rule-demo-spindle",
            Name = "主轴负载超过 90% 持续 5 分钟",
            Enabled = true,
            Scope = "device",
            OwnerId = "demo-fanuc",
            Expression = "spindleLoad > 90",
            DurationMs = 5 * 60 * 1000,
            DebounceMs = 60 * 1000,
            ActionsJson = RuleTemplates.All[0].ActionsJson,
            TemplateKey = "spindle-load",
            UpdatedUnixMs = now,
            UpdatedBy = "demo"
        });
        database.SetSetting("demoEdgeSeeded", "1");
    }

    private static string ReasonFor(string state, int index) => state switch
    {
        MachineState.Alarm => index % 2 == 0 ? "fault.mechanical" : "fault.tool",
        MachineState.Setup => "planned.changeover",
        MachineState.Waiting => "material.blank",
        MachineState.Planned => "planned.maintain",
        MachineState.Offline => "other.unknown",
        _ => index % 3 == 0 ? "people.operator" : "other.unknown"
    };
}
