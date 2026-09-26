using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Adapters.Cnc.Drivers;

/// <summary>
/// LSV2 telegrams as implemented by the public pyLSV2 description:
/// 4-byte big-endian payload length, 4-byte ASCII command, then payload.
/// </summary>
public static class Lsv2Codec
{
    public const string Login = "A_LG";
    public const string Logout = "A_LO";
    public const string ReadInfo = "R_RI";
    public const string ReadVersion = "R_VR";
    public const string Ok = "T_OK";
    public const string Error = "T_ER";
    public const string Info = "S_RI";
    public const string Version = "S_VR";

    public const ushort AxisLocation = 22;
    public const ushort ExecState = 23;
    public const ushort SelectedProgram = 24;
    public const ushort Override = 25;
    public const ushort ProgramState = 26;
    public const ushort FirstError = 27;
    public const byte VersionControl = 1;
    public const byte VersionNc = 2;

    public static byte[] Encode(string command, ReadOnlySpan<byte> payload)
    {
        if (command.Length != 4)
        {
            throw new ArgumentException("LSV2 command must be 4 characters.", nameof(command));
        }

        var buffer = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)payload.Length);
        Encoding.ASCII.GetBytes(command.AsSpan(), buffer.AsSpan(4, 4));
        payload.CopyTo(buffer.AsSpan(8));
        return buffer;
    }

    public static byte[] LoginPayload(string role, string? password)
    {
        var text = string.IsNullOrEmpty(password) ? role : role + "\0" + password;
        var bytes = new byte[Encoding.ASCII.GetByteCount(text) + 1];
        Encoding.ASCII.GetBytes(text, bytes);
        bytes[^1] = 0;
        return bytes;
    }

    public static byte[] UInt16(ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        return buffer;
    }

    public static byte[] Byte(byte value) => [value];

    public static bool TryReadFrame(ReadOnlySpan<byte> buffer, out Lsv2Frame frame, out int consumed)
    {
        frame = default;
        consumed = 0;
        if (buffer.Length < 8)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        if (buffer.Length < 8 + length)
        {
            return false;
        }

        var command = Encoding.ASCII.GetString(buffer.Slice(4, 4));
        var payload = buffer.Slice(8, (int)length).ToArray();
        frame = new Lsv2Frame(command, payload);
        consumed = 8 + (int)length;
        return true;
    }

    public static string CString(ReadOnlySpan<byte> payload)
    {
        var end = payload.IndexOf((byte)0);
        var slice = end >= 0 ? payload[..end] : payload;
        return Encoding.Latin1.GetString(slice).Trim();
    }

    public static string MapExecution(ushort value) => value switch
    {
        0 => "MANUAL",
        1 => "MDI",
        2 => "REFERENCE",
        3 => "SINGLE",
        4 => "AUTOMATIC",
        _ => "UNDEFINED"
    };

    public static string MapProgramState(ushort value) => value switch
    {
        0 => "STARTED",
        1 => "STOPPED",
        2 => "FINISHED",
        3 => "CANCELLED",
        4 => "INTERRUPTED",
        5 => "ERROR",
        6 => "ERROR_CLEARED",
        7 => "IDLE",
        _ => "UNDEFINED"
    };

    public static string MapMachineState(ushort execution, ushort programState)
    {
        if (programState == 5)
        {
            return "ALARM";
        }

        return execution is 3 or 4 ? "RUNNING" : "IDLE";
    }

    public static bool TryOverride(ReadOnlySpan<byte> payload, out double feed, out double spindle, out double rapid)
    {
        feed = spindle = rapid = 0;
        if (payload.Length < 12)
        {
            return false;
        }

        feed = BinaryPrimitives.ReadUInt32BigEndian(payload) / 100d;
        spindle = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]) / 100d;
        rapid = BinaryPrimitives.ReadUInt32BigEndian(payload[8..]) / 100d;
        return true;
    }

    public static bool TryProgram(ReadOnlySpan<byte> payload, out long line, out string main, out string current)
    {
        line = 0;
        main = "";
        current = "";
        if (payload.Length < 5)
        {
            return false;
        }

        line = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var parts = SplitStrings(payload[4..]);
        if (parts.Count > 0)
        {
            main = parts[0];
        }

        if (parts.Count > 1)
        {
            current = parts[1];
        }

        return main.Length > 0 || current.Length > 0;
    }

    public static bool TryError(ReadOnlySpan<byte> payload, out int number, out string text)
    {
        number = 0;
        text = "";
        if (payload.Length < 8)
        {
            return false;
        }

        number = BinaryPrimitives.ReadInt32BigEndian(payload[4..]);
        text = CString(payload[8..]);
        return true;
    }

    public static bool TryTool(ReadOnlySpan<byte> payload, out uint number)
    {
        number = 0;
        if (payload.Length < 4)
        {
            return false;
        }

        number = BinaryPrimitives.ReadUInt32BigEndian(payload);
        return true;
    }

    public static bool TryAxes(ReadOnlySpan<byte> payload, out IReadOnlyDictionary<string, double> axes)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        axes = map;
        if (payload.Length < 2)
        {
            return false;
        }

        var count = payload[1];
        var parts = SplitStrings(payload[2..]);
        if (count == 0 || parts.Count < count * 2)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var name = parts[i + count];
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var position))
            {
                map[name] = position;
            }
        }

        return map.Count > 0;
    }

    public static ushort UInt16Value(ReadOnlySpan<byte> payload) =>
        payload.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(payload) : (ushort)0;

    private static List<string> SplitStrings(ReadOnlySpan<byte> payload)
    {
        var rows = new List<string>();
        var start = 0;
        for (var i = 0; i <= payload.Length; i++)
        {
            if (i == payload.Length || payload[i] == 0)
            {
                if (i > start)
                {
                    rows.Add(Encoding.Latin1.GetString(payload[start..i]).Trim());
                }

                start = i + 1;
            }
        }

        return rows;
    }
}

public readonly record struct Lsv2Frame(string Command, byte[] Payload);
