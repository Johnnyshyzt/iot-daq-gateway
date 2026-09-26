using System.Buffers.Binary;
using System.Globalization;

namespace Adapters.Cnc.Drivers;

public static class ModbusCodec
{
    public static byte[] ReadHolding(ushort transaction, byte unit, ushort address, ushort count)
    {
        var buffer = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), 6);
        buffer[6] = unit;
        buffer[7] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(8), address);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(10), count);
        return buffer;
    }

    public static byte[] ReadInput(ushort transaction, byte unit, ushort address, ushort count)
    {
        var request = ReadHolding(transaction, unit, address, count);
        request[7] = 4;
        return request;
    }

    public static byte[] HoldingResponse(ushort transaction, byte unit, ushort[] registers)
    {
        var dataBytes = registers.Length * 2;
        var length = (ushort)(3 + dataBytes);
        var buffer = new byte[6 + length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), length);
        buffer[6] = unit;
        buffer[7] = 3;
        buffer[8] = (byte)dataBytes;
        for (var i = 0; i < registers.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(9 + i * 2), registers[i]);
        }

        return buffer;
    }

    public static bool TryRegisters(ReadOnlySpan<byte> frame, out ushort[] registers)
    {
        registers = [];
        if (frame.Length < 9)
        {
            return false;
        }

        var function = frame[7];
        if (function is 0x83 or 0x84)
        {
            return false;
        }

        var count = frame[8];
        if (function is not (3 or 4) || count % 2 != 0 || frame.Length < 9 + count)
        {
            return false;
        }

        registers = new ushort[count / 2];
        for (var i = 0; i < registers.Length; i++)
        {
            registers[i] = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(9 + i * 2, 2));
        }

        return true;
    }

    /// <summary>
    /// Gateway default holding-register layout. A point address <c>modbus:holding:N</c> or <c>modbus:input:N</c> overrides it.
    /// </summary>
    public static bool TryDefault(string itemId, out ushort address)
    {
        address = itemId.ToLowerInvariant() switch
        {
            "state" => 0,
            "alarm" or "alarmnumber" => 1,
            "program" => 2,
            "partcount" => 3,
            "spindlespeed" => 4,
            "spindlespeedcmd" => 5,
            "spindleoverride" => 6,
            "feedrate" => 7,
            "feedratecmd" => 8,
            "feedoverride" => 9,
            "machinepositionx" or "absolutpositionx" or "absolutepositionx" => 10,
            "machinepositiony" or "absolutpositiony" or "absolutepositiony" => 11,
            "machinepositionz" or "absolutpositionz" or "absolutepositionz" => 12,
            "toolnumber" => 13,
            "workmode" => 14,
            "partcounttotal" => 15,
            _ => ushort.MaxValue
        };
        return address != ushort.MaxValue;
    }

    public static bool TryAddress(string address, out bool input, out ushort register)
    {
        input = false;
        register = 0;
        if (string.IsNullOrWhiteSpace(address) || address.StartsWith("catalog/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = address;
        if (text.StartsWith("modbus:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["modbus:".Length..];
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out register))
        {
            return true;
        }

        if (parts.Length == 2 && ushort.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out register))
        {
            input = parts[0].Contains("input", StringComparison.OrdinalIgnoreCase);
            return parts[0].Contains("hold", StringComparison.OrdinalIgnoreCase) || input;
        }

        return false;
    }

    public static object ValueFor(string itemId, ushort raw)
    {
        if (itemId.Equals("state", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                1 => "RUNNING",
                2 => "ALARM",
                _ => "IDLE"
            };
        }

        if (itemId.Equals("program", StringComparison.OrdinalIgnoreCase))
        {
            return raw <= 9999 ? $"O{raw:D4}" : $"O{raw}";
        }

        if (itemId.Equals("workMode", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                1 => "MDI",
                2 => "JOG",
                3 => "EDIT",
                _ => "AUTO"
            };
        }

        if (itemId.Equals("alarm", StringComparison.OrdinalIgnoreCase))
        {
            return raw == 0 ? "0" : raw.ToString(CultureInfo.InvariantCulture);
        }

        return (int)raw;
    }
}
