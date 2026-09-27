namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Live acquisition session, when one is running. HTTP push does not use this.
/// </summary>
public interface IContractPublisher
{
    bool TryLease(out ContractLease? lease);
}

public sealed class ContractLease
{
    public required string GatewayId { get; init; }

    public required string Site { get; init; }

    public required string ContractVersion { get; init; }

    public required string TopicTemplate { get; init; }

    public required Func<string, string, CancellationToken, Task> PublishAsync { get; init; }
}
