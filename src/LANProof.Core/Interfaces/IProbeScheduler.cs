using LANProof.Core.Models;
namespace LANProof.Core.Interfaces;

/// <summary>Executes one bounded, parallel cycle. The runtime owns the periodic schedule.</summary>
public interface IProbeScheduler
{
    Task<IReadOnlyList<ProbeResult>> RunOnceAsync(CancellationToken cancellationToken = default);
}
