using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Adapters.Cnc.Drivers;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gateway.Tests;

public sealed class ProtocolDriverTests
{
    [Fact]
    public void MtConnect_xml_maps_execution_program_and_axes()
    {
        const string xml = """
            <MTConnectStreams>
              <Streams>
                <DeviceStream name="Mazak">
                  <ComponentStream component="Path" name="path">
                    <Events>
                      <Execution dataItemId="ex">ACTIVE</Execution>
                      <ControllerMode dataItemId="mode">AUTOMATIC</ControllerMode>
                      <EmergencyStop dataItemId="estop">ARMED</EmergencyStop>
                      <Program dataItemId="pgm">O1000</Program>
                      <ProgramComment dataItemId="cmt">COVER</ProgramComment>
                      <PartCount dataItemId="pc" name="PartCount">4</PartCount>
                    </Events>
                    <Samples>
                      <PathFeedrate dataItemId="f" subType="ACTUAL">1200</PathFeedrate>
                      <SpindleSpeed dataItemId="s" subType="ACTUAL">3000</SpindleSpeed>
                      <Position dataItemId="x" name="X">1.5</Position>
                      <Position dataItemId="y" name="Y">2</Position>
                    </Samples>
                  </ComponentStream>
                </DeviceStream>
              </Streams>
            </MTConnectStreams>
            """;

        var values = MtConnectXml.ToCatalog(MtConnectXml.Parse(xml));
        Assert.Equal("RUNNING", values["state"]);
        Assert.Equal("AUTOMATIC", values["workMode"]);
        Assert.Equal("O1000", values["program"]);
        Assert.Equal("COVER", values["programComment"]);
        Assert.Equal(4L, values["partCount"]);
        Assert.Equal(1200L, values["feedRate"]);
        Assert.Equal(3000L, values["spindleSpeed"]);
        Assert.Equal("0", values["alarm"]);
        Assert.Contains("X1.5", Convert.ToString(values["machinePosition"], CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Lsv2_frame_roundtrip_and_status_decoders()
    {
        var login = Lsv2Codec.Encode(Lsv2Codec.Login, Lsv2Codec.LoginPayload("INSPECT", null));
        Assert.True(Lsv2Codec.TryReadFrame(login, out var frame, out var consumed));
        Assert.Equal(login.Length, consumed);
        Assert.Equal(Lsv2Codec.Login, frame.Command);
        Assert.Equal("INSPECT", Lsv2Codec.CString(frame.Payload));

        var payload = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(payload, 15000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), 8000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), 10000);
        Assert.True(Lsv2Codec.TryOverride(payload, out var feed, out var spindle, out var rapid));
        Assert.Equal(150, feed);
        Assert.Equal(80, spindle);
        Assert.Equal(100, rapid);
        Assert.Equal("ALARM", Lsv2Codec.MapMachineState(4, 5));
        Assert.Equal("RUNNING", Lsv2Codec.MapMachineState(4, 0));
        Assert.Equal("IDLE", Lsv2Codec.MapMachineState(0, 7));

        var program = new byte[] { 0, 0, 0, 12, (byte)'T', (byte)'N', (byte)'C', 0, (byte)'O', (byte)'1', 0 };
        Assert.True(Lsv2Codec.TryProgram(program, out var line, out var main, out var current));
        Assert.Equal(12, line);
        Assert.Equal("TNC", main);
        Assert.Equal("O1", current);
    }

    [Fact]
    public void Haas_q_replies_map_status_time_and_parts()
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        HaasQCodec.Apply("Q100", "SERIAL NUMBER, 12345", values);
        HaasQCodec.Apply("Q300", "POWER ON TIME, 001:02:03", values);
        HaasQCodec.Apply("Q500", "PROGRAM, O0100, STATUS, FEED HOLD, PARTS, 9", values);
        Assert.Equal("12345", values["serialNumber"]);
        Assert.Equal(3723L, values["powerOnTime"]);
        Assert.Equal("O0100", values["program"]);
        Assert.Equal("RUNNING", values["state"]);
        Assert.Equal(9L, values["partCount"]);
        Assert.Equal("?Q104\n", HaasQCodec.Query("Q104"));
    }

    [Fact]
    public async Task Modbus_in_process_server_reads_the_default_map()
    {
        var registers = new ushort[16];
        registers[0] = 1;
        registers[2] = 1000;
        registers[4] = 2500;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var port = await ModbusServer.Start(registers, cts.Token);
        var adapter = CncCatalog.Current.FindAdapter("delta.modbus");
        Assert.NotNull(adapter);
        var driver = new ModbusDriver(Binding("delta-1", "delta", port), adapter, NullLogger.Instance);
        var rows = await driver.CollectAsync(CancellationToken.None);
        Assert.Contains(rows, row => row.Point == "state" && Equals(row.Value, "RUNNING"));
        Assert.Contains(rows, row => row.Point == "program" && Equals(row.Value, "O1000"));
        Assert.Contains(rows, row => row.Point == "spindleSpeed" && Equals(row.Value, 2500));
    }

    [Fact]
    public void Modbus_response_frame_matches_the_codec()
    {
        var frame = ModbusCodec.HoldingResponse(7, 1, [1, 1000]);
        Assert.True(ModbusCodec.TryRegisters(frame, out var registers));
        Assert.Equal(new ushort[] { 1, 1000 }, registers);
        Assert.Equal("RUNNING", ModbusCodec.ValueFor("state", 1));
    }

    [Fact]
    public async Task Haas_q_tcp_prompt_returns_a_sample()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var port = await HaasServer.Start(cts.Token);
        var adapter = CncCatalog.Current.FindAdapter("haas.q");
        Assert.NotNull(adapter);
        var driver = new HaasQDriver(Binding("haas-1", "haas", port, timeout: 2000), adapter, NullLogger.Instance);
        var rows = await driver.CollectAsync(CancellationToken.None);
        Assert.Contains(rows, row => row.Point == "serialNumber" && Equals(row.Value, "ABC"));
        Assert.Contains(rows, row => row.Point == "state" && Equals(row.Value, "IDLE"));
    }

    [Fact]
    public void Ftp_status_file_keeps_catalog_keys()
    {
        var values = FtpStatus.Parse("state=RUNNING\nprogram,O2000\nspindleSpeed=1800\n");
        Assert.Equal("RUNNING", values["state"]);
        Assert.Equal("O2000", values["program"]);
        Assert.Equal(1800L, values["spindleSpeed"]);
    }

    [Fact]
    public async Task Ftp_loopback_downloads_the_status_file()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var port = await FtpServer.Start("state=IDLE\n", cts.Token);
        var body = await FtpStatus.DownloadAsync("127.0.0.1", port, "/status.txt", "anonymous", "", 2000, CancellationToken.None);
        Assert.Equal("IDLE", FtpStatus.Parse(body)["state"]);
    }

    [Fact]
    public async Task Siemens_opcua_map_reads_program_status_as_running()
    {
        var adapter = CncCatalog.Current.FindAdapter("siemens.opcua");
        Assert.NotNull(adapter);
        var driver = new OpcUaDriver(
            Binding("siemens-1", "siemens", 4840),
            adapter,
            NullLogger.Instance,
            () => new FakeOpcSession());
        var rows = await driver.CollectAsync(CancellationToken.None);
        Assert.Contains(rows, row => row.Point == "state" && Equals(row.Value, "RUNNING"));
        Assert.Contains(rows, row => row.Point == "program" && Equals(row.Value, "O1000"));
        Assert.Contains(rows, row => row.Point == "workMode" && Equals(row.Value, "mode:1"));
    }

    [Fact]
    public async Task Missing_vendor_sdk_reports_the_install_path()
    {
        var directory = Directory.CreateTempSubdirectory("sdk-missing");
        var adapter = CncCatalog.Current.FindAdapter("syntec.custom");
        Assert.NotNull(adapter);
        var binding = Binding("syntec-1", "syntec", 1, timeout: 400);
        binding.Options["sdkPath"] = directory.FullName;
        var driver = new VendorSdkDriver(binding, adapter, NullLogger.Instance);
        var report = await driver.ProbeAsync(CancellationToken.None);
        Assert.False(report.Handshake);
        Assert.Equal("missing", report.SdkStatus);
        Assert.Contains("SDK 未安装", report.Message, StringComparison.Ordinal);
        Assert.Contains(directory.FullName, report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Brother_driver_does_not_invent_a_handshake()
    {
        Assert.Contains("CNC-C00", BrotherCncDriver.Reason, StringComparison.Ordinal);
        Assert.Contains("brother.mtconnect", BrotherCncDriver.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_support_marks_siemens_opc_and_focas_sdk()
    {
        var siemens = DriverCatalog.Describe("siemens");
        var opc = Assert.Single(siemens.Drivers, driver => driver.AdapterId == "siemens.opcua");
        Assert.Equal(DriverCatalog.Supported, Assert.Single(opc.Items, item => item.ItemId == "state").Level);
        Assert.Equal(DriverCatalog.NotAvailable, Assert.Single(opc.Items, item => item.ItemId == "softwareVersion").Level);

        var fanuc = DriverCatalog.Describe("fanuc");
        var focas = Assert.Single(fanuc.Drivers, driver => driver.AdapterId == "fanuc.focas");
        Assert.Equal(DriverCatalog.ViaSdk, Assert.Single(focas.Items, item => item.ItemId == "spindleSpeed").Level);
    }

    private static DeviceBinding Binding(string id, string brand, int port, int timeout = 2000) => new()
    {
        Id = id,
        Enabled = true,
        Adapter = brand,
        Options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "127.0.0.1",
            ["port"] = port,
            ["timeoutMs"] = timeout,
            ["brandId"] = brand,
            ["unitId"] = 1
        }
    };

    private sealed class FakeOpcSession : IOpcUaSession
    {
        public Task ConnectAsync(OpcConnectInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

        public int ResolveNamespace(string? uriHint, string fallbackContains) => 2;

        public Task<IReadOnlyDictionary<string, OpcReading>> ReadAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, OpcReading>(StringComparer.Ordinal);
            foreach (var node in nodeIds)
            {
                object value = 1;
                if (node.Contains("progStatus", StringComparison.Ordinal))
                {
                    value = 3;
                }
                else if (node.Contains("actAlarm", StringComparison.Ordinal))
                {
                    value = 0;
                }
                else if (node.Contains("progName", StringComparison.Ordinal))
                {
                    value = "O1000";
                }

                map[node] = new OpcReading(value, null);
            }

            return Task.FromResult<IReadOnlyDictionary<string, OpcReading>>(map);
        }

        public Task<IReadOnlyDictionary<string, string>> BrowseNamesAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static class ModbusServer
    {
        public static async Task<int> Start(ushort[] registers, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                        _ = Handle(client, registers, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    listener.Stop();
                }
            }, cancellationToken);
            return port;
        }

        private static async Task Handle(TcpClient client, ushort[] registers, CancellationToken cancellationToken)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var request = new byte[12];
                await ReadExact(stream, request, cancellationToken).ConfigureAwait(false);
                var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8));
                var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10));
                var slice = new ushort[count];
                for (var i = 0; i < count; i++)
                {
                    var index = address + i;
                    slice[i] = index < registers.Length ? registers[index] : (ushort)0;
                }

                var response = ModbusCodec.HoldingResponse(
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request),
                    request[6],
                    slice);
                if (request[7] == 4)
                {
                    response[7] = 4;
                }

                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static class HaasServer
    {
        public static async Task<int> Start(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(">"), cancellationToken).ConfigureAwait(false);
                    var buffer = new StringBuilder();
                    var bytes = new byte[128];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        buffer.Append(Encoding.ASCII.GetString(bytes, 0, read));
                        var text = buffer.ToString();
                        var newline = text.IndexOf('\n');
                        if (newline < 0)
                        {
                            continue;
                        }

                        var query = text[..newline].Trim();
                        buffer.Clear();
                        buffer.Append(text[(newline + 1)..]);
                        var reply = query.Contains("Q100", StringComparison.Ordinal)
                            ? "SERIAL, ABC>"
                            : query.Contains("Q500", StringComparison.Ordinal)
                                ? "STATUS, IDLE>"
                                : "0>";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    listener.Stop();
                }
            }, cancellationToken);
            return port;
        }
    }

    private static class FtpServer
    {
        public static async Task<int> Start(string body, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                    await writer.WriteLineAsync("220 ready".AsMemory(), cancellationToken).ConfigureAwait(false);
                    TcpListener? data = null;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line is null)
                        {
                            break;
                        }

                        if (line.StartsWith("USER", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("230 ok".AsMemory(), cancellationToken).ConfigureAwait(false);
                        }
                        else if (line.StartsWith("TYPE", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("200 ok".AsMemory(), cancellationToken).ConfigureAwait(false);
                        }
                        else if (line.StartsWith("PASV", StringComparison.OrdinalIgnoreCase))
                        {
                            data = new TcpListener(IPAddress.Loopback, 0);
                            data.Start();
                            var dataPort = ((IPEndPoint)data.LocalEndpoint).Port;
                            await writer.WriteLineAsync($"227 Entering Passive Mode (127,0,0,1,{dataPort / 256},{dataPort % 256})".AsMemory(), cancellationToken).ConfigureAwait(false);
                        }
                        else if (line.StartsWith("RETR", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("150 ok".AsMemory(), cancellationToken).ConfigureAwait(false);
                            using var dataClient = await data!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                            await using var dataStream = dataClient.GetStream();
                            var payload = Encoding.UTF8.GetBytes(body);
                            await dataStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                            dataClient.Close();
                            await writer.WriteLineAsync("226 done".AsMemory(), cancellationToken).ConfigureAwait(false);
                            break;
                        }
                        else
                        {
                            await writer.WriteLineAsync("502 unsupported".AsMemory(), cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    listener.Stop();
                }
            }, cancellationToken);
            return port;
        }
    }

    private static async Task ReadExact(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("socket closed");
            }

            offset += read;
        }
    }
}
