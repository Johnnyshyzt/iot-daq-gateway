using System.Text;

namespace Gateway.Host.Logging;

internal static class GatewayLogFiles
{
    public static List<string> Tail(int lines)
    {
        lines = Math.Clamp(lines, 1, 2000);
        var directory = FileLoggingExtensions.ResolveDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var file = Directory.GetFiles(directory, "gateway-*.log")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (file is null)
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var all = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                all.Add(line);
            }

            return all.TakeLast(lines).ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
