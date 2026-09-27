using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Adds computed points (and rule writes) to a sweep before samples are stored and published.
/// </summary>
public interface IObservationExpander
{
    IReadOnlyList<Observation> Expand(IReadOnlyList<Observation> observations);
}
