using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Adapters.Cnc.Drivers;

public static class FtpStatus
{
    public static IReadOnlyDictionary<string, object?> Parse(string body)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('#'))
            {
                continue;
            }

            var split = raw.Split(['=', ','], 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2 || string.IsNullOrWhiteSpace(split[0]))
            {
                continue;
            }

            values[split[0]] = Coerce(split[1]);
        }

        return values;
    }

    public static async Task<string> DownloadAsync(
        string host,
        int port,
        string path,
        string username,
        string password,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeoutMs);
        await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await ExpectAsync(reader, 220, linked.Token).ConfigureAwait(false);
        await writer.WriteLineAsync($"USER {(string.IsNullOrWhiteSpace(username) ? "anonymous" : username)}".AsMemory(), linked.Token).ConfigureAwait(false);
        var user = await ReadReplyAsync(reader, linked.Token).ConfigureAwait(false);
        if (user.Code == 331)
        {
            await writer.WriteLineAsync($"PASS {password}".AsMemory(), linked.Token).ConfigureAwait(false);
            user = await ReadReplyAsync(reader, linked.Token).ConfigureAwait(false);
        }

        if (user.Code is not (230 or 202))
        {
            throw new InvalidOperationException($"FTP 登录失败：{user.Text}");
        }

        await writer.WriteLineAsync("TYPE I".AsMemory(), linked.Token).ConfigureAwait(false);
        await ExpectAsync(reader, 200, linked.Token).ConfigureAwait(false);
        await writer.WriteLineAsync("PASV".AsMemory(), linked.Token).ConfigureAwait(false);
        var passive = await ReadReplyAsync(reader, linked.Token).ConfigureAwait(false);
        if (passive.Code != 227)
        {
            throw new InvalidOperationException($"FTP PASV 失败：{passive.Text}");
        }

        var endpoint = ParsePassive(passive.Text);
        using var dataClient = new TcpClient();
        await dataClient.ConnectAsync(endpoint.Address, endpoint.Port, linked.Token).ConfigureAwait(false);
        var remote = string.IsNullOrWhiteSpace(path) ? "/status.txt" : path;
        await writer.WriteLineAsync($"RETR {remote}".AsMemory(), linked.Token).ConfigureAwait(false);
        var retr = await ReadReplyAsync(reader, linked.Token).ConfigureAwait(false);
        if (retr.Code is not (150 or 125))
        {
            throw new InvalidOperationException($"FTP RETR 失败：{retr.Text}");
        }

        await using var data = dataClient.GetStream();
        using var dataReader = new StreamReader(data, Encoding.UTF8);
        var body = await dataReader.ReadToEndAsync(linked.Token).ConfigureAwait(false);
        await ReadReplyAsync(reader, linked.Token).ConfigureAwait(false);
        return body;
    }

    public static IPEndPoint ParsePassive(string text)
    {
        var open = text.IndexOf('(');
        var close = text.IndexOf(')', open + 1);
        if (open < 0 || close < 0)
        {
            throw new FormatException("FTP PASV 应答没有地址");
        }

        var parts = text[(open + 1)..close].Split(',');
        if (parts.Length != 6
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hi)
            || !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo))
        {
            throw new FormatException("FTP PASV 地址无效");
        }

        var ip = string.Join('.', parts.Take(4).Select(part => part.Trim()));
        return new IPEndPoint(IPAddress.Parse(ip), hi * 256 + lo);
    }

    private static object Coerce(string value)
    {
        if (bool.TryParse(value, out var flag))
        {
            return flag;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value;
    }

    private static async Task ExpectAsync(StreamReader reader, int code, CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(reader, cancellationToken).ConfigureAwait(false);
        if (reply.Code != code)
        {
            throw new InvalidOperationException($"FTP 期望 {code}，收到 {reply.Text}");
        }
    }

    private static async Task<FtpReply> ReadReplyAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null || line.Length < 3 || !int.TryParse(line[..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            throw new IOException("FTP 应答无效");
        }

        var text = line;
        if (line.Length > 3 && line[3] == '-')
        {
            while (true)
            {
                var next = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (next is null)
                {
                    break;
                }

                text = next;
                if (next.StartsWith(code.ToString(CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        return new FtpReply(code, text);
    }

    private readonly record struct FtpReply(int Code, string Text);
}
