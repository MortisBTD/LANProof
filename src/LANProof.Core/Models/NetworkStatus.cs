namespace LANProof.Core.Models;

/// <summary>
/// Current snapshot of the network status for the dashboard.
/// </summary>
public sealed class NetworkStatus
{
    /// <summary>Overall connection health.</summary>
    public HealthLevel Health { get; set; }

    /// <summary>Current ping to gateway in ms.</summary>
    public double? GatewayPingMs { get; set; }

    /// <summary>Current ping to external hosts in ms.</summary>
    public double? ExternalPingMs { get; set; }

    /// <summary>Current jitter in ms.</summary>
    public double? JitterMs { get; set; }

    /// <summary>Current packet loss percentage.</summary>
    public double? PacketLossPercent { get; set; }

    /// <summary>Current DNS resolution time in ms.</summary>
    public double? DnsResolveMs { get; set; }

    /// <summary>Wi-Fi signal strength in dBm.</summary>
    public int? SignalStrengthDbm { get; set; }

    /// <summary>Active connection type.</summary>
    public ConnectionType ConnectionType { get; set; }

    /// <summary>Gateway IP address.</summary>
    public string? GatewayAddress { get; set; }

    /// <summary>Local IP address.</summary>
    public string? LocalAddress { get; set; }

    /// <summary>Adapter name.</summary>
    public string? AdapterName { get; set; }

    /// <summary>Timestamp of this snapshot.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Number of active events.</summary>
    public int ActiveEventCount { get; set; }

    /// <summary>Last event description.</summary>
    public string? LastEventSummary { get; set; }
}
