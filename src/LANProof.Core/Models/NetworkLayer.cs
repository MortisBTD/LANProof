namespace LANProof.Core.Models;

/// <summary>
/// Network diagnostic layer.
/// </summary>
public enum NetworkLayer
{
    /// <summary>Device level — adapter, driver, OS networking.</summary>
    Device,

    /// <summary>Local network — connection to router/gateway.</summary>
    LocalNetwork,

    /// <summary>Internet — connectivity beyond the gateway.</summary>
    Internet,

    /// <summary>DNS — domain name resolution.</summary>
    Dns,

    /// <summary>Service — specific website or game server.</summary>
    Service
}
