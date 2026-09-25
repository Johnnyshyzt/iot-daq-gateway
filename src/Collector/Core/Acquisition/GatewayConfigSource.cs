namespace Gateway.Host.Acquisition;

internal sealed class GatewayConfigSource(string path)
{
    public string Path { get; } = path;
}
