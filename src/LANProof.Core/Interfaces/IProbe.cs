namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Interface for all network probes.
/// </summary>
public interface IProbe
{
    /// <summary>Type of this probe.</summary>
    ProbeType Type { get; }

    /// <summary>Human-readable name of this probe.</summary>
    string Name { get; }

    /// <summary>Execute the probe and return results.</summary>
    Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
