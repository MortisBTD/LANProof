namespace LANProof.Core.Models;

/// <summary>
/// Overall network health assessment level.
/// </summary>
public enum HealthLevel
{
    /// <summary>Everything is working well.</summary>
    Healthy,

    /// <summary>Minor issues detected but connection works.</summary>
    Degraded,

    /// <summary>Significant problems affecting connectivity.</summary>
    Unstable,

    /// <summary>No internet connection.</summary>
    Offline
}
