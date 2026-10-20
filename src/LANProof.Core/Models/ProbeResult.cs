namespace LANProof.Core.Models;

/// <summary>
/// Represents the result of a single network probe execution.
/// </summary>
public sealed class ProbeResult
{
    /// <summary>Unique identifier for this result.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp when the probe was executed.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Type of probe that produced this result.</summary>
    public ProbeType ProbeType { get; set; }

    /// <summary>Target host or IP that was probed.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Whether the probe was successful.</summary>
    public bool IsSuccess { get; set; }
    /// <summary>False when the cycle budget or an internal error prevented an observation.</summary>
    public bool IsConclusive { get; set; } = true;
    /// <summary>Clock alignment uncertainty; null means no trusted alignment for cross-device analysis.</summary>
    public double? ClockUncertaintyMs { get; set; } = 0;

    /// <summary>Round-trip time in milliseconds. Null if the probe failed.</summary>
    public double? RoundTripMs { get; set; }

    /// <summary>Jitter (variation in latency) in milliseconds. Calculated over a batch.</summary>
    public double? JitterMs { get; set; }

    /// <summary>Packet loss percentage (0-100). Calculated over a batch.</summary>
    public double? PacketLossPercent { get; set; }

    /// <summary>DNS resolution time in milliseconds (for DNS probes).</summary>
    public double? DnsResolveMs { get; set; }

    /// <summary>HTTP status code (for HTTP probes).</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Wi-Fi signal strength in dBm (for adapter probes).</summary>
    public int? SignalStrengthDbm { get; set; }

    /// <summary>Name of the network adapter.</summary>
    public string? AdapterName { get; set; }

    /// <summary>Connection type (Ethernet, Wi-Fi, etc.).</summary>
    public ConnectionType ConnectionType { get; set; }

    /// <summary>Device identifier for multi-device correlation.</summary>
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>Additional details or error message.</summary>
    public string? Details { get; set; }
}
