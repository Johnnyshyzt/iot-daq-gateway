using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

/// <summary>
/// Extension point for a real protocol driver. Phase 2 replaces the body of
/// <see cref="CollectAsync"/> for one protocol family. This stub never loads a vendor SDK.
/// </summary>
public class Phase2DriverStub : ISouthboundAdapter
{
    private readonly ILogger _logger;
    private readonly CatalogAdapter _adapter;

    public Phase2DriverStub(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    {
        _logger = logger;
        _adapter = adapter;
        DeviceId = binding.Id;
        AdapterKind = adapter.Id;
    }

    public string AdapterKind { get; }

    public string DeviceId { get; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Adapter {Adapter} on {DeviceId} is a phase-2 stub ({Protocol}). Use the brand simulator until the driver is implemented.",
            AdapterKind,
            DeviceId,
            _adapter.Protocol);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Observation>>([]);
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = AdapterStatus.Offline,
            Message = $"第二阶段才会实现 {_adapter.Protocol} 驱动（{AdapterKind}）。当前请使用该品牌的模拟器。",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
