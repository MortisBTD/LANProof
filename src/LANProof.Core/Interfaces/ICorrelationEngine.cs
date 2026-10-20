namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Correlates events across devices and network layers to identify root causes.
/// </summary>
public interface ICorrelationEngine
{
    /// <summary>
    /// Analyze events and measurements within a time window and produce a diagnostic report.
    /// </summary>
    DiagnosticReport Analyze(
        IReadOnlyList<NetworkEvent> events,
        IReadOnlyList<ProbeResult> measurements,
        DateTime periodStart,
        DateTime periodEnd);
}
