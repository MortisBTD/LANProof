namespace LANProof.Core.Models;

/// <summary>
/// Category of network event.
/// </summary>
public enum EventCategory
{
    /// <summary>Latency spike detected.</summary>
    LatencySpike,

    /// <summary>Packet loss detected.</summary>
    PacketLoss,

    /// <summary>Complete connection loss.</summary>
    ConnectionLost,

    /// <summary>Connection restored after outage.</summary>
    ConnectionRestored,

    /// <summary>DNS resolution failure or slowdown.</summary>
    DnsIssue,

    /// <summary>Wi-Fi signal degradation.</summary>
    WiFiSignalDrop,

    /// <summary>High jitter detected.</summary>
    HighJitter,

    /// <summary>Adapter disconnected or changed.</summary>
    AdapterChange
}
