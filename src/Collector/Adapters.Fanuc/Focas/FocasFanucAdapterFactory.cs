using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Focas;

public sealed class FocasFanucAdapterFactory : ISouthboundAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFocasLibrary _library;

    public FocasFanucAdapterFactory(ILoggerFactory loggerFactory)
        : this(loggerFactory, NativeFocasLibrary.Instance)
    {
    }

    internal FocasFanucAdapterFactory(ILoggerFactory loggerFactory, IFocasLibrary library)
    {
        _loggerFactory = loggerFactory;
        _library = library;
    }

    public string AdapterKind => FocasFanucAdapter.Kind;

    public ISouthboundAdapter Create(DeviceBinding binding)
    {
        return new FocasFanucAdapter(
            binding.Id,
            binding.Options,
            _loggerFactory.CreateLogger<FocasFanucAdapter>(),
            _library);
    }
}
