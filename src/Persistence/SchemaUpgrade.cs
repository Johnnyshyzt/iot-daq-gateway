using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

/// <summary>
/// Brings an existing SQLite or PostgreSQL file up to <see cref="GatewayPersistence.SchemaVersion"/>.
/// Fresh databases already match the model after <c>EnsureCreated</c>; the statements are idempotent.
/// </summary>
internal static class SchemaUpgrade
{
    public static void Apply(GatewayDbContext db, bool sqlite)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            EnsureFromScript(db, connection, "state_transitions");
            EnsureFromScript(db, connection, "app_settings");
            if (sqlite)
            {
                AddColumn(connection, "alarms", "Code", "TEXT NOT NULL DEFAULT ''");
                AddColumn(connection, "alarms", "ClearedUnixMs", "INTEGER NULL");
                AddColumn(connection, "alarms", "DurationMs", "INTEGER NULL");
                AddColumn(connection, "alarms", "Acknowledged", "INTEGER NOT NULL DEFAULT 0");
                AddColumn(connection, "alarms", "AcknowledgedBy", "TEXT NULL");
                AddColumn(connection, "alarms", "AcknowledgedUnixMs", "INTEGER NULL");
            }
            else
            {
                AddColumn(connection, "alarms", "Code", "text NOT NULL DEFAULT ''");
                AddColumn(connection, "alarms", "ClearedUnixMs", "bigint NULL");
                AddColumn(connection, "alarms", "DurationMs", "bigint NULL");
                AddColumn(connection, "alarms", "Acknowledged", "boolean NOT NULL DEFAULT false");
                AddColumn(connection, "alarms", "AcknowledgedBy", "text NULL");
                AddColumn(connection, "alarms", "AcknowledgedUnixMs", "bigint NULL");
            }

            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_sample_history_device_point_time ON sample_history ("DeviceId", "PointId", "TimestampUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_sample_history_time ON sample_history ("TimestampUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_sample_history_device_time ON sample_history ("DeviceId", "TimestampUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_alarms_device_raised ON alarms ("DeviceId", "RaisedUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_alarms_active_raised ON alarms ("Active", "RaisedUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_alarms_device_code ON alarms ("DeviceId", "Code")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_state_device_started ON state_transitions ("DeviceId", "StartedUnixMs")""");
            Execute(connection, """CREATE INDEX IF NOT EXISTS ix_state_ended ON state_transitions ("EndedUnixMs")""");
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void EnsureFromScript(GatewayDbContext db, DbConnection connection, string table)
    {
        if (TableExists(connection, table))
        {
            return;
        }

        var script = db.Database.GenerateCreateScript();
        foreach (var statement in Split(script))
        {
            if (statement.Contains(table, StringComparison.OrdinalIgnoreCase)
                && statement.Contains("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                Execute(connection, statement);
            }
        }
    }

    private static bool TableExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_name = @name";
        if (connection.GetType().Name.Contains("Sqlite", StringComparison.Ordinal))
        {
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name";
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() is not null;
    }

    private static void AddColumn(DbConnection connection, string table, string column, string definition)
    {
        if (ColumnExists(connection, table, column))
        {
            return;
        }

        Execute(connection, $"ALTER TABLE {table} ADD COLUMN \"{column}\" {definition}");
    }

    private static bool ColumnExists(DbConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        if (connection.GetType().Name.Contains("Sqlite", StringComparison.Ordinal))
        {
            command.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = @name";
        }
        else
        {
            command.CommandText = "SELECT 1 FROM information_schema.columns WHERE table_name = @table AND column_name = @name";
            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = table;
            command.Parameters.Add(tableParameter);
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = column;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() is not null;
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Split(string script)
    {
        var statements = new List<string>();
        var builder = new StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            builder.AppendLine(line);
            if (line.TrimEnd().EndsWith(';'))
            {
                var text = builder.ToString().Trim();
                if (text.Length > 0)
                {
                    statements.Add(text);
                }

                builder.Clear();
            }
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
        {
            statements.Add(tail);
        }

        return statements;
    }
}
