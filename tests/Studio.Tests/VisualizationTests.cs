using System.Globalization;
using Gateway.Abstractions.Models;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Studio.Tests;

public sealed class VisualizationTests
{
    private static readonly ShiftCalendar Shifts = ShiftCalendar.Default();

    [Fact]
    public void Shift_boundary_splits_run_time_and_scales_an_open_shift()
    {
        var from = Local(2026, 9, 26, 8, 0);
        var to = Local(2026, 9, 26, 21, 0);
        var segment = new StateSegment(
            "cnc",
            "running",
            Local(2026, 9, 26, 19, 30).ToUnixTimeMilliseconds(),
            Local(2026, 9, 26, 20, 30).ToUnixTimeMilliseconds());
        var rows = UtilizationMath.Build(
            [new DeviceRef { Id = "cnc", DisplayName = "车床", Workshop = "一车间", Line = "车削线" }],
            [segment],
            [],
            Shifts,
            from,
            to,
            to);

        var day = Assert.Single(rows, row => row.Shift == "白班");
        var night = Assert.Single(rows, row => row.Shift == "夜班");
        Assert.Equal(30 * 60_000, day.RunMs);
        Assert.Equal(660 * 60_000, day.PlannedMs);
        Assert.Equal(30 * 60_000, night.RunMs);
        Assert.Equal(55 * 60_000, night.PlannedMs);
        Assert.Equal(0.5455, night.Utilization, 4);
    }

    [Fact]
    public void Offline_gap_is_not_counted_as_run_time()
    {
        var calendar = new ShiftCalendar
        {
            TimeZone = "Asia/Shanghai",
            Shifts = [new ShiftDefinition { Name = "白班", Start = "08:00", End = "12:00", PlannedMinutes = 240 }]
        };
        var from = Local(2026, 9, 26, 8, 0);
        var to = Local(2026, 9, 26, 12, 0);
        var segments = new[]
        {
            new StateSegment("cnc", "running", from.ToUnixTimeMilliseconds(), Local(2026, 9, 26, 9, 0).ToUnixTimeMilliseconds()),
            new StateSegment("cnc", "running", Local(2026, 9, 26, 10, 0).ToUnixTimeMilliseconds(), Local(2026, 9, 26, 11, 0).ToUnixTimeMilliseconds())
        };
        var row = Assert.Single(UtilizationMath.Build([new DeviceRef { Id = "cnc" }], segments, [], calendar, from, to, to));
        Assert.Equal(2 * 60 * 60_000, row.RunMs);
        Assert.Equal(2 * 60 * 60_000, row.OfflineMs);
        Assert.Equal(0.5, row.Utilization, 3);
    }

    [Theory]
    [InlineData(new[] { 10d, 20d, 5d, 8d }, 18d)]
    [InlineData(new[] { 100d, 0d, 4d }, 4d)]
    [InlineData(new[] { 7d }, 0d)]
    [InlineData(new[] { 3d, 3d, 9d }, 6d)]
    public void Counter_reset_adds_the_new_reading_instead_of_a_negative_delta(double[] values, double expected)
    {
        var samples = values.Select((value, index) => new PartSample("cnc", index * 1000, value)).ToList();
        Assert.Equal(expected, UtilizationMath.PartDelta(samples));
    }

    [Fact]
    public void Part_counts_resetting_across_a_shift_stay_in_that_shift()
    {
        var from = Local(2026, 9, 26, 8, 0);
        var to = Local(2026, 9, 27, 8, 0);
        var parts = new[]
        {
            new PartSample("cnc", Local(2026, 9, 26, 19, 0).ToUnixTimeMilliseconds(), 10),
            new PartSample("cnc", Local(2026, 9, 26, 19, 50).ToUnixTimeMilliseconds(), 16),
            new PartSample("cnc", Local(2026, 9, 26, 20, 10).ToUnixTimeMilliseconds(), 2),
            new PartSample("cnc", Local(2026, 9, 26, 20, 40).ToUnixTimeMilliseconds(), 5)
        };
        var rows = UtilizationMath.Build([new DeviceRef { Id = "cnc" }], [], parts, Shifts, from, to, to);
        var day = Assert.Single(rows, row => row.Shift == "白班" && row.Day == "2026-09-26");
        var night = Assert.Single(rows, row => row.Shift == "夜班" && row.Day == "2026-09-26");
        Assert.Equal(6, day.PartCount);
        Assert.Equal(5, night.PartCount);
    }

    [Fact]
    public void Alarm_codes_come_from_catalog_alarm_text_and_state()
    {
        Assert.True(AlarmLogic.IsActive("state", "ALARM"));
        Assert.False(AlarmLogic.IsActive("state", "RUNNING"));
        Assert.False(AlarmLogic.IsActive("alarm", "0"));
        Assert.True(AlarmLogic.IsActive("alarm", "EX100 伺服过载"));
        Assert.Equal("EX100", AlarmLogic.CodeFrom("alarm", "EX100 伺服过载"));
        Assert.Equal("EX231", AlarmLogic.CodeFrom("alarmNumber", "EX231"));
        Assert.Equal("STATE", AlarmLogic.CodeFrom("state", "ALARM"));
        Assert.True(AlarmLogic.IsActive("estop", "true"));
        Assert.Equal(180_000, AlarmLogic.Duration(1_000, 181_000, 999_000, active: false));
        Assert.Equal(50, AlarmLogic.Duration(100, null, 150, active: true));
    }

    [Fact]
    public void Top_alarms_rank_by_count_then_duration()
    {
        var alarms = new[]
        {
            new AlarmStatInput { DeviceId = "a", Code = "EX100", Message = "伺服过载", DurationMs = 10 },
            new AlarmStatInput { DeviceId = "a", Code = "EX100", Message = "伺服过载", DurationMs = 30 },
            new AlarmStatInput { DeviceId = "b", Code = "PS101", Message = "程序错误", DurationMs = 1000 },
            new AlarmStatInput { DeviceId = "a", Code = "PS101", Message = "程序错误", DurationMs = 5 }
        };
        var (byDevice, byCode) = AlarmStats.Top(alarms, 1);
        Assert.Equal("a", Assert.Single(byDevice).Key);
        Assert.Equal(3, byDevice[0].Count);
        Assert.Equal("PS101", Assert.Single(byCode).Key);
        Assert.Equal(2, byCode[0].Count);
    }

    [Fact]
    public void Series_buckets_average_min_and_max()
    {
        Assert.Equal(5_000, SeriesAggregation.ChooseBucket(0, 60 * 60_000, 0));
        Assert.Equal(300_000, SeriesAggregation.ChooseBucket(0, 24 * 60 * 60_000, 0));
        var samples = new[]
        {
            new SeriesSample("cnc", "spindleSpeed", 1_000, 10),
            new SeriesSample("cnc", "spindleSpeed", 2_000, 30),
            new SeriesSample("cnc", "spindleSpeed", 70_000, 5)
        };
        var points = SeriesAggregation.Aggregate(samples, 60_000);
        Assert.Equal(2, points.Count);
        Assert.Equal(20, points[0].Avg);
        Assert.Equal(10, points[0].Min);
        Assert.Equal(30, points[0].Max);
        Assert.Equal(2, points[0].Count);
    }

    [Fact]
    public void Overlapping_shifts_are_rejected()
    {
        var calendar = new ShiftCalendar
        {
            Shifts =
            [
                new ShiftDefinition { Name = "白班", Start = "08:00", End = "20:00", PlannedMinutes = 600 },
                new ShiftDefinition { Name = "中班", Start = "16:00", End = "22:00", PlannedMinutes = 300 }
            ]
        };
        Assert.Contains(calendar.Validate(), issue => issue.Contains("重叠", StringComparison.Ordinal));
    }

    [Fact]
    public void Stored_samples_derive_alarms_states_and_aggregated_series()
    {
        var directory = Directory.CreateTempSubdirectory("viz-store").FullName;
        try
        {
            var database = GatewayPersistence.Open(directory, "Sqlite", null, 14);
            var start = DateTimeOffset.Parse("2026-09-26T01:00:00Z", CultureInfo.InvariantCulture);
            database.Write(
            [
                Sample("cnc", "state", "RUNNING", start),
                Sample("cnc", "alarm", "0", start),
                Sample("cnc", "spindleSpeed", 1000d, start),
                Sample("cnc", "partCount", 10d, start)
            ]);
            database.Write(
            [
                Sample("cnc", "state", "ALARM", start.AddMinutes(5)),
                Sample("cnc", "alarm", "EX100 伺服过载", start.AddMinutes(5)),
                Sample("cnc", "alarmNumber", "EX100", start.AddMinutes(5)),
                Sample("cnc", "spindleSpeed", 40d, start.AddMinutes(5)),
                Sample("cnc", "partCount", 12d, start.AddMinutes(5))
            ]);
            database.Write(
            [
                Sample("cnc", "state", "IDLE", start.AddMinutes(8)),
                Sample("cnc", "alarm", "0", start.AddMinutes(8)),
                Sample("cnc", "partCount", 3d, start.AddMinutes(8))
            ]);

            var alarms = database.QueryAlarms("cnc", null, null, null, null, null, 20);
            var alarm = Assert.Single(alarms);
            Assert.Equal("EX100", alarm.Code);
            Assert.Equal("EX100 伺服过载", alarm.Message);
            Assert.False(alarm.Active);
            Assert.Equal(3 * 60_000, alarm.DurationMs);

            var acked = database.AcknowledgeAlarm(alarm.Id, "engineer", start.AddMinutes(9).ToUnixTimeMilliseconds());
            Assert.NotNull(acked);
            Assert.True(acked.Acknowledged);
            Assert.Equal("engineer", acked.AcknowledgedBy);
            Assert.False(acked.Active);

            var states = database.Transitions(["cnc"], 0, long.MaxValue);
            Assert.Equal(["running", "alarm", "idle"], states.Select(state => state.State).ToArray());

            var series = database.AggregateSeries(["cnc"], ["spindleSpeed"], 0, long.MaxValue, 60_000);
            Assert.Equal(2, series.Count);
            Assert.Equal(1000, series[0].Avg);
            Assert.Equal(40, series[1].Max);

            var parts = database.PartSamples(["cnc"], 0, long.MaxValue, 60_000);
            Assert.Equal(5, UtilizationMath.PartDelta(parts));

            database.SetSetting("historyRetentionDays", "30");
            Assert.Equal(30, database.HistoryRetentionDays);
            database.EnsureReady();
            Assert.Equal(30, database.HistoryRetentionDays);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void Existing_sqlite_file_gains_alarm_columns_and_state_table()
    {
        var directory = Directory.CreateTempSubdirectory("viz-migrate").FullName;
        try
        {
            var database = GatewayPersistence.Open(directory, "Sqlite", null, 9);
            database.EnsureReady();
            var path = Path.Combine(directory, "gateway.db");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE alarms_old (
                      Id TEXT NOT NULL PRIMARY KEY,
                      DeviceId TEXT NOT NULL,
                      PointId TEXT NOT NULL,
                      Message TEXT NOT NULL,
                      Severity TEXT NOT NULL,
                      Active INTEGER NOT NULL,
                      RaisedUnixMs INTEGER NOT NULL
                    );
                    INSERT INTO alarms_old (Id, DeviceId, PointId, Message, Severity, Active, RaisedUnixMs)
                      SELECT Id, DeviceId, PointId, Message, Severity, Active, RaisedUnixMs FROM alarms;
                    DROP TABLE alarms;
                    ALTER TABLE alarms_old RENAME TO alarms;
                    DROP TABLE state_transitions;
                    DROP TABLE app_settings;
                    UPDATE schema_info SET Version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            database.EnsureReady();
            database.Write([Sample("cnc", "state", "ALARM", DateTimeOffset.UnixEpoch.AddHours(2))]);
            var alarm = Assert.Single(database.ListAlarms("cnc", 10));
            Assert.Equal("STATE", alarm.Code);
            Assert.True(alarm.Active);
            Assert.NotEmpty(database.Transitions(["cnc"], 0, long.MaxValue));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static Observation Sample(string deviceId, string point, object value, DateTimeOffset timestamp) => new()
    {
        DeviceId = deviceId,
        Point = point,
        Value = value,
        Timestamp = timestamp,
        Quality = "good"
    };

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
