using Gateway.Abstractions.Configuration;

namespace Gateway.Host.Acquisition;

internal sealed class GatewayConfigHolder(GatewayConfiguration current)
{
    public GatewayConfiguration Current { get; set; } = current;
}
