namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Detects network events (anomalies) from probe results.
/// </summary>
public interface IEventDetector
{
    /// <summary>Fired when a new network event is detected.</summary>
    event EventHandler<NetworkEvent>? EventDetected;

    /// <summary>Fired when an ongoing event is resolved.</summary>
    event EventHandler<NetworkEvent>? EventResolved;

    /// <summary>Process a new probe result and check for events.</summary>
    void ProcessResult(ProbeResult result);

    /// <summary>Get all currently ongoing events.</summary>
    IReadOnlyList<NetworkEvent> GetActiveEvents();
}
