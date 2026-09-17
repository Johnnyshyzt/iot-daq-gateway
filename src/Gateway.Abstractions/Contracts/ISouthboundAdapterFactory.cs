using Gateway.Abstractions.Configuration;

namespace Gateway.Abstractions.Contracts;

public interface ISouthboundAdapterFactory
{
    string AdapterKind { get; }

    ISouthboundAdapter Create(DeviceBinding binding);
}
