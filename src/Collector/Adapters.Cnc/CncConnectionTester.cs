using System.Diagnostics;
using Adapters.Cnc.Drivers;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

public sealed class CncConnectionTester(ILoggerFactory loggerFactory) : IDeviceConnectionTester
{
    public bool CanTest(string? adapterId)
    {
        var adapter = CncCatalog.Current.FindAdapter(adapterId);
        return adapter is not null
            && adapter.Protocol is not ("focas" or "fake" or "sim");
    }

    public async Task<DeviceConnectionReport> TestAsync(DeviceConnectionRequest request, CancellationToken cancellationToken)
    {
        var adapter = CncCatalog.Current.FindAdapter(request.Adapter);
        if (adapter is null)
        {
            return new DeviceConnectionReport
            {
                Message = $"未知适配器 {request.Adapter}",
                Error = "unknown_adapter"
            };
        }

        if (adapter.Kind == "simulator" || adapter.Protocol is "sim" or "fake")
        {
            return new DeviceConnectionReport
            {
                Ok = true,
                Handshake = true,
                LatencyMs = 1,
                HandshakeMs = 1,
                SdkStatus = "none",
                Message = "模拟器握手成功（未连接真实机床）"
            };
        }

        var options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Options)
        {
            options[pair.Key] = pair.Value;
        }

        options["brandId"] = request.BrandId;
        options["controllerModelId"] = request.ControllerModelId;
        var binding = new DeviceBinding
        {
            Id = string.IsNullOrWhiteSpace(request.DeviceId) ? "connection-test" : request.DeviceId,
            Enabled = true,
            Adapter = adapter.Id,
            Options = options
        };

        var driver = CncDriverFactory.Create(binding, adapter, loggerFactory);
        var watch = Stopwatch.StartNew();
        try
        {
            if (driver is not IConnectionProbe probe)
            {
                return new DeviceConnectionReport
                {
                    Message = "该适配器没有连接测试。",
                    SdkStatus = "none"
                };
            }

            using var trace = ProtocolTraceHub.Begin(binding.Id);
            var report = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            watch.Stop();
            return new DeviceConnectionReport
            {
                Ok = report.Ok,
                Reachable = report.Reachable,
                Handshake = report.Handshake,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                ReachableMs = report.ReachableMs,
                HandshakeMs = report.HandshakeMs,
                Message = report.Message,
                Error = report.Error,
                SdkStatus = report.SdkStatus,
                Samples = report.Samples.Take(12).Select(sample => new DeviceConnectionSample
                {
                    Point = sample.Point,
                    Value = PointValues.Format(sample.Value),
                    Quality = sample.Quality,
                    Unit = sample.Unit
                }).ToList()
            };
        }
        finally
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }
    }
}
