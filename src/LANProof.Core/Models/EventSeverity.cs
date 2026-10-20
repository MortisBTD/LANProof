namespace LANProof.Core.Models;

/// <summary>
/// Severity level of a network event.
/// </summary>
public enum EventSeverity
{
    /// <summary>Informational — minor fluctuation.</summary>
    Info,

    /// <summary>Warning — noticeable degradation.</summary>
    Warning,

    /// <summary>Critical — connection lost or severely degraded.</summary>
    Critical
}
