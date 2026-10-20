namespace LANProof.Core.Models;

/// <summary>
/// Type of network connection.
/// </summary>
public enum ConnectionType
{
    /// <summary>Unknown connection type.</summary>
    Unknown,

    /// <summary>Wired Ethernet connection.</summary>
    Ethernet,

    /// <summary>Wi-Fi wireless connection.</summary>
    WiFi,

    /// <summary>Cellular data connection (mobile).</summary>
    Cellular
}
