using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Fake;

public sealed class FakeFanucAdapterFactory : ISouthboundAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public FakeFanucAdapterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string AdapterKind => FakeFanucAdapter.Kind;

    public ISouthboundAdapter Create(DeviceBinding binding)
    {
        return new FakeFanucAdapter(
            binding.Id,
            binding.Options,
            _loggerFactory.CreateLogger<FakeFanucAdapter>());
    }
}
