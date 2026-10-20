namespace LANProof.Core.Models;

/// <summary>
/// Represents a detected network event (anomaly, outage, recovery).
/// </summary>
public sealed class NetworkEvent
{
    /// <summary>Unique identifier.</summary>
    public long Id { get; set; }

    /// <summary>When the event started (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the event ended (UTC). Null if ongoing.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Severity of the event.</summary>
    public EventSeverity Severity { get; set; }

    /// <summary>Category of the event.</summary>
    public EventCategory Category { get; set; }

    /// <summary>Network layer where the event was detected.</summary>
    public NetworkLayer Layer { get; set; }

    /// <summary>Human-readable title of the event.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detailed description of what happened.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Device where the event was detected.</summary>
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>Connection type at the time of the event.</summary>
    public ConnectionType ConnectionType { get; set; }

    /// <summary>Whether this event is still ongoing.</summary>
    public bool IsOngoing => EndTime is null;

    /// <summary>Duration of the event.</summary>
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
}
