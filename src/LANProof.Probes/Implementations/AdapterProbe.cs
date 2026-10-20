using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Implementations;

/// <summary>
/// Checks network adapter status and Wi-Fi signal strength.
/// </summary>
public sealed partial class AdapterProbe : IProbe
{
    private readonly ILogger<AdapterProbe> _logger;

    /// <inheritdoc />
    public ProbeType Type => ProbeType.Adapter;

    /// <inheritdoc />
    public string Name => "Adapter Status";

    public AdapterProbe(ILogger<AdapterProbe> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = new ProbeResult
        {
            ProbeType = ProbeType.Adapter,
            Target = "local"
        };

        try
        {
            var activeAdapter = NetworkHelper.GetActiveNetworkInterface();

            if (activeAdapter is null)
            {
                result.IsSuccess = false;
                result.ConnectionType = ConnectionType.Unknown;
                result.Details = "No active network adapter with gateway found.";
                return result;
            }

            result.IsSuccess = true;
            result.AdapterName = activeAdapter.Name;
            result.ConnectionType = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                ? ConnectionType.WiFi
                : ConnectionType.Ethernet;

            // Get Wi-Fi signal strength using netsh on Windows
            if (result.ConnectionType == ConnectionType.WiFi && OperatingSystem.IsWindows())
            {
                result.SignalStrengthDbm = await GetWiFiSignalStrengthAsync(cancellationToken);
            }

            var ipProps = activeAdapter.GetIPProperties();
            var unicastAddress = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            result.Details = $"Adapter: {activeAdapter.Name}, " +
                             $"Type: {result.ConnectionType}, " +
                             $"Speed: {activeAdapter.Speed / 1_000_000}Mbps, " +
                             $"IP: {unicastAddress?.Address}";

            if (result.SignalStrengthDbm.HasValue)
            {
                result.Details += $", Signal: {result.SignalStrengthDbm}dBm";
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter probe failed");
            result.IsSuccess = false;
            result.Details = $"Error: {ex.Message}";
        }

        return result;
    }

    private async Task<int?> GetWiFiSignalStrengthAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(2000);
            string output;
            try
            {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited) process.Kill();
            }

            // Parse signal quality percentage (e.g., "Signal : 85%")
            var match = SignalRegex().Match(output);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int signalPercent))
            {
                // Convert percentage to approximate dBm: dBm ≈ (quality / 2) - 100
                return (signalPercent / 2) - 100;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Wi-Fi signal strength");
        }

        return null;
    }

    [GeneratedRegex(@"(?:Signal|Сигнал)\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex SignalRegex();
}
