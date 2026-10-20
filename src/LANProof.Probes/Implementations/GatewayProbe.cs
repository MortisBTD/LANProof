using System.Net;
using System.Net.NetworkInformation;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Implementations;

/// <summary>
/// Probes the default gateway with ICMP ping to check local network connectivity.
/// </summary>
public sealed class GatewayProbe : IProbe
{
    private readonly ILogger<GatewayProbe> _logger;
    private readonly ProbeConfiguration _config;

    /// <inheritdoc />
    public ProbeType Type => ProbeType.Gateway;

    /// <inheritdoc />
    public string Name => "Gateway Ping";

    public GatewayProbe(ILogger<GatewayProbe> logger, ProbeConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = new ProbeResult
        {
            ProbeType = ProbeType.Gateway,
            ConnectionType = NetworkHelper.GetConnectionType()
        };

        try
        {
            var gateway = NetworkHelper.GetDefaultGateway();
            if (gateway is null)
            {
                result.IsSuccess = false;
                result.Details = "No default gateway found.";
                return result;
            }

            result.Target = gateway.ToString();
            result.AdapterName = NetworkHelper.GetActiveAdapterName();

            using var ping = new Ping();
            var roundTrips = new List<double>();
            int failures = 0;

            for (int i = 0; i < _config.PingsPerCycle; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var reply = await ping.SendPingAsync(gateway, TimeSpan.FromMilliseconds(_config.IcmpTimeoutMs), cancellationToken: cancellationToken);
                    if (reply.Status == IPStatus.Success)
                    {
                        roundTrips.Add(reply.RoundtripTime);
                    }
                    else
                    {
                        failures++;
                    }
                }
                catch (PingException)
                {
                    failures++;
                }
            }

            if (roundTrips.Count > 0)
            {
                result.IsSuccess = true;
                result.RoundTripMs = roundTrips.Average();
                result.JitterMs = roundTrips.Count > 1 ? CalculateJitter(roundTrips) : null;
                result.PacketLossPercent = (double)failures / _config.PingsPerCycle * 100;
                result.Details = $"{gateway}: avg={result.RoundTripMs:F1}ms, loss={failures}/{_config.PingsPerCycle}";
            }
            else
            {
                result.IsSuccess = false;
                result.PacketLossPercent = 100;
                result.Details = "All pings to gateway failed.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway probe failed");
            result.IsSuccess = false;
            result.Details = $"Error: {ex.Message}";
        }

        return result;
    }

    private static double CalculateJitter(List<double> values)
    {
        if (values.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < values.Count; i++)
        {
            sum += Math.Abs(values[i] - values[i - 1]);
        }
        return sum / (values.Count - 1);
    }
}
