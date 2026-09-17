using Adapters.Fanuc.Focas;
using Gateway.Host;
using Gateway.Host.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gateway.Tests;

public sealed class HostInfoTests
{
    [Fact]
    public void Version_IsNonEmptyAndStartsWithAssemblyVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(HostInfo.Version));
        Assert.StartsWith("0.3.0", HostInfo.Version, StringComparison.Ordinal);
        Assert.Equal("IotDaqGateway", HostInfo.WindowsServiceName);
    }
}

public sealed class RollingFileLoggerTests
{
    [Fact]
    public void FormatLine_IncludesCalendarDate()
    {
        var line = RollingFileLoggerProvider.FormatLine(
            "Gateway.Host",
            LogLevel.Information,
            new EventId(0),
            "iot-daq-gateway 0.3.0 starting",
            exception: null);

        Assert.Contains("Gateway.Host", line, StringComparison.Ordinal);
        Assert.Contains("iot-daq-gateway 0.3.0 starting", line, StringComparison.Ordinal);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} ", line);
    }

    [Fact]
    public void WritesAndRetainsOpsLogFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "iot-daq-gateway-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var provider = new RollingFileLoggerProvider(dir))
            {
                var logger = provider.CreateLogger("Gateway.Host");
                logger.LogInformation("iot-daq-gateway {Version} for ops", HostInfo.Version);
            }

            var files = Directory.GetFiles(dir, "gateway-*.log");
            Assert.Single(files);
            var text = File.ReadAllText(files[0]);
            Assert.Contains("iot-daq-gateway", text, StringComparison.Ordinal);
            Assert.Contains(HostInfo.Version, text, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // temp cleanup is best-effort
            }
        }
    }
}

public sealed class FocasLibraryFilesTests
{
    [Fact]
    public void DocumentsDllNextToProcess_NeverShipsBinary()
    {
        Assert.Equal("Fwlib64.dll", FocasLibraryFiles.WindowsDll);
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "Fwlib64.dll"),
            FocasLibraryFiles.ExpectedPath);
        Assert.False(File.Exists(FocasLibraryFiles.ExpectedPath));
    }
}
