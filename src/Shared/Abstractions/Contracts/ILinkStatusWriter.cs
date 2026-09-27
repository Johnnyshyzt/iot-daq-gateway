namespace Gateway.Abstractions.Contracts;

public interface ILinkStatusWriter
{
    void Upsert(string deviceId, string status, string? message, DateTimeOffset timestamp);
}
