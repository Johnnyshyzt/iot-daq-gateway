using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Focas;

public sealed class FocasFanucAdapterFactory : ISouthboundAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public FocasFanucAdapterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string AdapterKind => FocasFanucAdapter.Kind;

    public ISouthboundAdapter Create(DeviceBinding binding)
    {
        return new FocasFanucAdapter(
            binding.Id,
            binding.Options,
            _loggerFactory.CreateLogger<FocasFanucAdapter>());
    }
}
