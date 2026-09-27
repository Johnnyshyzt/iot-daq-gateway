using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IotDaq.Licensing;
using Studio.Host.Config;
using Studio.Host.Licensing;

namespace Studio.Host.Commissioning;

public static class DiagnosticBundle
{
    public static string WriteZip(string outputPath, ConfigStore store, LicenseService licensing, ProtocolTraceBuffer traces)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        AddJson(archive, "versions.json", new
        {
            product = "iot-daq-gateway",
            version = InformationalVersion(),
            schemaVersion = store.Database.CurrentSchemaVersion,
            database = store.Database.Provider,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            process = Environment.Is64BitProcess ? "x64" : "x86"
        });
        var license = licensing.Describe(store);
        AddJson(archive, "license-summary.json", new
        {
            license.Status,
            license.Edition,
            license.Customer,
            license.Message,
            license.DeviceLimit,
            license.PointLimit,
            license.DraftDevices,
            license.DraftPoints,
            license.PublishedDevices,
            license.PublishedPoints,
            license.ExpiresAt,
            license.GraceUntil,
            license.TamperCode,
            license.BlocksConfig,
            license.MachineFingerprint,
            license.BoundFingerprint,
            license.CollectionContinues,
            note = "不含许可证原文、私钥或本机状态密钥。"
        });
        AddText(archive, "config/draft.json", SecretRedactor.RedactJson(store.ExportJson(false)));
        AddText(archive, "config/published.json", SecretRedactor.RedactJson(store.ExportJson(true)));
        AddJson(archive, "runtime/link-status.json", store.Database.ListLinkStatus());
        AddJson(archive, "runtime/self-tests.json", store.Database.ListSelfTests(20).Select(row => new
        {
            row.Id,
            row.DeviceId,
            row.Passed,
            row.Summary,
            row.StartedUnixMs,
            row.FinishedUnixMs,
            stages = Parse(row.StagesJson)
        }));
        AddJson(archive, "runtime/spool.json", new
        {
            mqtt = Size(Path.Combine(store.DataDirectory, "mqtt-spool")),
            http = Size(Path.Combine(store.DataDirectory, "http-spool"))
        });
        AddText(archive, "logs/gateway-tail.log", TailLog(store.DataDirectory));
        CopyTraces(archive, traces.DirectoryPath);
        AddJson(archive, "system.json", new
        {
            machine = Environment.MachineName,
            processors = Environment.ProcessorCount,
            utc = DateTimeOffset.UtcNow,
            dataDirectory = store.DataDirectory,
            checklist = "docs/manual/现场调试清单.md"
        });
        AddText(archive, "README.txt", """
            采集网关诊断包。
            配置、报文和日志里的口令、密钥和证书密码已打码。
            不含 data/auth、data/security/state.key、许可证原文和 HTTPS 私钥。
            """);
        return outputPath;
    }

    public static string DefaultPath(string dataDirectory)
    {
        var name = "iot-daq-gateway-diagnose-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
        return Path.Combine(dataDirectory, "diagnostics", name);
    }

    private static void CopyTraces(ZipArchive archive, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            var redacted = new StringBuilder();
            foreach (var line in File.ReadLines(file))
            {
                redacted.AppendLine(SecretRedactor.Redact(line));
            }

            AddText(archive, "traces/" + Path.GetFileName(file), redacted.ToString());
        }
    }

    private static string TailLog(string dataDirectory)
    {
        var directory = Path.Combine(dataDirectory, "logs");
        if (!Directory.Exists(directory))
        {
            return "";
        }

        var file = Directory.GetFiles(directory, "gateway-*.log").OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();
        if (file is null)
        {
            return "";
        }

        try
        {
            var lines = File.ReadLines(file).TakeLast(400).Select(line => SecretRedactor.Redact(line));
            return string.Join('\n', lines);
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static object Size(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new { exists = false, files = 0, bytes = 0L };
        }

        long bytes = 0;
        var files = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files++;
            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                continue;
            }
        }

        return new { exists = true, files, bytes };
    }

    private static object Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return Array.Empty<object>();
        }
    }

    private static void AddJson(ZipArchive archive, string name, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        AddText(archive, name, SecretRedactor.RedactJson(json));
    }

    private static void AddText(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
    }

    private static string InformationalVersion()
    {
        var assembly = typeof(DiagnosticBundle).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
