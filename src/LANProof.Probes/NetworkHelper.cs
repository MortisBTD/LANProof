using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LANProof.Core.Models;

namespace LANProof.Probes;

/// <summary>
/// Helper utilities for network information discovery.
/// </summary>
public static class NetworkHelper
{
    /// <summary>
    /// Get the default gateway IP address.
    /// </summary>
    public static IPAddress? GetDefaultGateway()
    {
        return (GetActiveNetworkInterface()?.GetIPProperties().GatewayAddresses.Select(g => g.Address) ?? [])
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork &&
                                 !IPAddress.IsLoopback(a) &&
                                 !a.Equals(IPAddress.None));
    }

    /// <summary>
    /// Get the active network connection type.
    /// </summary>
    public static ConnectionType GetConnectionType()
    {
        var activeAdapter = GetActiveNetworkInterface();
        if (activeAdapter is null) return ConnectionType.Unknown;

        return activeAdapter.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => ConnectionType.Ethernet,
            NetworkInterfaceType.Wireless80211 => ConnectionType.WiFi,
            _ => ConnectionType.Unknown
        };
    }

    /// <summary>
    /// Get the name of the active network adapter.
    /// </summary>
    public static string? GetActiveAdapterName()
    {
        return GetActiveNetworkInterface()?.Name;
    }

    /// <summary>
    /// Get the local IP address of the active adapter.
    /// </summary>
    public static IPAddress? GetLocalIPAddress()
    {
        var adapter = GetActiveNetworkInterface();
        if (adapter is null) return null;

        return adapter.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address;
    }

    public static NetworkInterface? GetActiveNetworkInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.Wireless80211 &&
                n.GetIPProperties().GatewayAddresses.Count > 0);
    }
}
