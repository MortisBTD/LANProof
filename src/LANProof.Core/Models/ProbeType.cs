namespace LANProof.Core.Models;

/// <summary>
/// Defines the types of network probes available in LANProof.
/// </summary>
public enum ProbeType
{
    /// <summary>ICMP ping to the default gateway.</summary>
    Gateway,

    /// <summary>ICMP ping to external IP addresses.</summary>
    External,

    /// <summary>DNS resolution test.</summary>
    Dns,

    /// <summary>HTTP/HTTPS endpoint availability check.</summary>
    Http,

    /// <summary>Network adapter and Wi-Fi signal check.</summary>
    Adapter
}
