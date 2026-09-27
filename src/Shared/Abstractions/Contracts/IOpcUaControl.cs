namespace Gateway.Abstractions.Contracts;

public interface IOpcUaControl
{
    OpcUaRuntimeInfo Current { get; }

    void Reload();
}

public sealed class OpcUaRuntimeInfo
{
    public bool Enabled { get; set; }

    public bool Listening { get; set; }

    public int Port { get; set; }

    public string Endpoint { get; set; } = "";

    public string LastError { get; set; } = "";
}
