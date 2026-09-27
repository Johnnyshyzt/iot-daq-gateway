using Gateway.Host.Reliability;
using Sinks.Mqtt;
using Xunit;

namespace Gateway.Tests;

public sealed class ReconnectBackoffTests
{
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(8);

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 4000)]
    [InlineData(4, 8000)]
    [InlineData(8, 8000)]
    public void Delay_doubles_until_the_cap(int attempt, int expectedMs)
    {
        var delay = ReconnectBackoff.Delay(attempt, Initial, 2, Cap, 0.2, 0.5);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), delay);
    }

    [Fact]
    public void Jitter_stays_inside_the_ratio_and_never_passes_the_cap()
    {
        var low = ReconnectBackoff.Delay(1, Initial, 2, Cap, 0.2, 0);
        var high = ReconnectBackoff.Delay(1, Initial, 2, Cap, 0.2, 1);
        Assert.Equal(TimeSpan.FromMilliseconds(800), low);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), high);

        var capped = ReconnectBackoff.Delay(6, Initial, 2, Cap, 0.2, 1);
        Assert.Equal(Cap, capped);
    }
}

public sealed class DeviceLinkBookTests
{
    [Fact]
    public void Backoff_skips_until_the_retry_time_then_a_stall_is_visible()
    {
        var book = new DeviceLinkBook();
        var options = new Gateway.Abstractions.Reliability.ReliabilityOptions
        {
            ReconnectInitialSeconds = 2,
            ReconnectMultiplier = 2,
            ReconnectCapSeconds = 8,
            ReconnectJitter = 0
        };
        var start = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        Assert.True(book.ShouldAttempt("cnc-01", start));
        book.BeginAttempt("cnc-01", start);
        Assert.Empty(book.Stalled(start.AddSeconds(5), TimeSpan.FromSeconds(30)));
        Assert.Contains("cnc-01", book.Stalled(start.AddSeconds(30), TimeSpan.FromSeconds(30)));

        book.Failed("cnc-01", "连接被拒绝", start, options, 0.5);
        var link = book.Find("cnc-01");
        Assert.Equal(DeviceLinkPhase.Backoff, link!.Phase);
        Assert.Equal(start.AddSeconds(2), link.NextRetryUtc);
        Assert.Equal("连接被拒绝", link.LastError);
        Assert.False(book.ShouldAttempt("cnc-01", start.AddSeconds(1)));
        Assert.True(book.ShouldAttempt("cnc-01", start.AddSeconds(2)));
        Assert.Empty(book.Stalled(start.AddSeconds(40), TimeSpan.FromSeconds(30)));

        book.Connected("cnc-01", start.AddSeconds(3));
        var online = book.Find("cnc-01");
        Assert.Equal(DeviceLinkPhase.Connected, online!.Phase);
        Assert.Equal(0, online.Attempt);
        Assert.Null(online.NextRetryUtc);
    }
}

public sealed class MqttSpoolTests
{
    [Fact]
    public void Drops_oldest_and_replays_the_rest_in_order()
    {
        var directory = Directory.CreateTempSubdirectory("mqtt-spool").FullName;
        try
        {
            var now = new DateTimeOffset(2026, 9, 27, 1, 0, 0, TimeSpan.Zero);
            var spool = new MqttSpool(directory, new ReliabilityLimits
            {
                MaxMessages = () => 3,
                MaxAge = () => TimeSpan.FromHours(24),
                MaxBytes = () => 1024 * 1024
            });
            for (var i = 1; i <= 5; i++)
            {
                spool.Enqueue("daq/plant/cnc/" + i, "{\"n\":" + i + "}", 1, false, now.AddSeconds(i));
            }

            Assert.Equal(3, spool.Depth);
            Assert.Equal(2, spool.Dropped);
            var first = spool.PeekOldest(now.AddMinutes(1));
            Assert.NotNull(first);
            Assert.Contains("\"n\":3", first!.Payload, StringComparison.Ordinal);
            spool.Acknowledge(first.Seq);
            var second = spool.PeekOldest(now.AddMinutes(1));
            Assert.Contains("\"n\":4", second!.Payload, StringComparison.Ordinal);
            Assert.True(second.Seq > first.Seq);

            var reopened = new MqttSpool(directory, new ReliabilityLimits
            {
                MaxMessages = () => 3,
                MaxAge = () => TimeSpan.FromHours(24),
                MaxBytes = () => 1024 * 1024
            });
            Assert.Equal(2, reopened.Dropped);
            Assert.Equal(2, reopened.Depth);
            Assert.Equal(second.Seq, reopened.PeekOldest(now.AddMinutes(1))!.Seq);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Drops_messages_older_than_the_age_limit()
    {
        var directory = Directory.CreateTempSubdirectory("mqtt-spool-age").FullName;
        try
        {
            var now = new DateTimeOffset(2026, 9, 27, 1, 0, 0, TimeSpan.Zero);
            var spool = new MqttSpool(directory, new ReliabilityLimits
            {
                MaxMessages = () => 10,
                MaxAge = () => TimeSpan.FromHours(1),
                MaxBytes = () => 1024 * 1024
            });
            spool.Enqueue("old", "{}", 0, false, now);
            spool.Enqueue("fresh", "{}", 0, false, now.AddHours(2));
            var fresh = spool.PeekOldest(now.AddHours(2));
            Assert.Equal("fresh", fresh!.Topic);
            Assert.Equal(1, spool.Depth);
            Assert.True(spool.Dropped >= 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class ServiceRecoveryTests
{
    [Fact]
    public void Windows_installer_and_systemd_restart_on_failure()
    {
        var unit = File.ReadAllText(RepoFile("packaging/linux/iot-daq-gateway.service"));
        Assert.Contains("Restart=on-failure", unit, StringComparison.Ordinal);
        Assert.Contains("RestartSec=5", unit, StringComparison.Ordinal);

        var installer = File.ReadAllText(RepoFile("packaging/windows/install-service.bat"));
        Assert.Contains("sc failure", installer, StringComparison.Ordinal);
        Assert.Contains("restart/5000/restart/10000/restart/30000", installer, StringComparison.Ordinal);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path))
            {
                return path;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
