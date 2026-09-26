using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public abstract class CatalogProtocolDriver : ISouthboundAdapter, IConnectionProbe
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _message;
    private AdapterStatus _status = AdapterStatus.Offline;

    protected CatalogProtocolDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    {
        Binding = binding;
        Adapter = adapter;
        Logger = logger;
        DeviceId = binding.Id;
        AdapterKind = adapter.Id;
        Settings = ConnectionSettings.Read(binding);
        _message = $"{adapter.DisplayName} 尚未连接";
    }

    protected DeviceBinding Binding { get; }

    protected CatalogAdapter Adapter { get; }

    protected ILogger Logger { get; }

    protected ConnectionSettings Settings { get; }

    public string AdapterKind { get; }

    public string DeviceId { get; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OnConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetOffline(ex.Message);
            Logger.LogWarning(ex, "{Adapter} {DeviceId} connect failed", AdapterKind, DeviceId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var filtered = rows.Where(row => Settings.Wants(row.Point)).ToList();
            if (filtered.Count > 0)
            {
                SetOnline($"{Adapter.DisplayName} 已读取 {filtered.Count} 个点");
            }

            return filtered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetOffline(ex.Message);
            Logger.LogWarning(ex, "{Adapter} {DeviceId} collect failed", AdapterKind, DeviceId);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = _status,
            Message = _message,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public abstract Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await OnDisconnectAsync().ConfigureAwait(false);
            SetOffline("已断开");
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    protected virtual Task OnConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task OnDisconnectAsync() => Task.CompletedTask;

    protected abstract Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken);

    protected void SetOnline(string message)
    {
        _status = AdapterStatus.Online;
        _message = message;
    }

    protected void SetDegraded(string message)
    {
        _status = AdapterStatus.Degraded;
        _message = message;
    }

    protected void SetOffline(string message)
    {
        _status = AdapterStatus.Offline;
        _message = message;
    }

    protected string ItemUnit(string itemId) =>
        CncCatalog.Current.FindItem(itemId)?.Unit ?? "";
}
