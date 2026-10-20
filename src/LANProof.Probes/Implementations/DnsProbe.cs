using System.Diagnostics;
using System.Net;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Implementations;

/// <summary>
/// Tests DNS resolution speed and reliability.
/// </summary>
public sealed class DnsProbe : IProbe
{
    private readonly ILogger<DnsProbe> _logger;
    private readonly ProbeConfiguration _config;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    /// <inheritdoc />
    public ProbeType Type => ProbeType.Dns;

    /// <inheritdoc />
    public string Name => "DNS Resolution";

    public DnsProbe(ILogger<DnsProbe> logger, ProbeConfiguration config,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        _logger = logger;
        _config = config;
        _resolve = resolve ?? ((domain, ct) => Dns.GetHostAddressesAsync(domain, ct));
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = new ProbeResult
        {
            ProbeType = ProbeType.Dns,
            ConnectionType = NetworkHelper.GetConnectionType(),
            AdapterName = NetworkHelper.GetActiveAdapterName()
        };

        var resolveTimes = new List<double>();
        int failures = 0;
        var details = new List<string>();

        foreach (var domain in _config.DnsTestDomains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sw = Stopwatch.StartNew();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_config.TimeoutMs);
                var addresses = await _resolve(domain, timeout.Token);
                sw.Stop();

                if (addresses.Length > 0)
                {
                    resolveTimes.Add(sw.Elapsed.TotalMilliseconds);
                    details.Add($"{domain}: {sw.Elapsed.TotalMilliseconds:F1}ms → {addresses[0]}");
                }
                else
                {
                    failures++;
                    details.Add($"{domain}: no addresses returned");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
                details.Add($"{domain}: FAILED ({ex.Message})");
            }
        }

        result.Target = string.Join(", ", _config.DnsTestDomains);

        if (resolveTimes.Count > 0)
        {
            result.IsSuccess = true;
            result.DnsResolveMs = resolveTimes.Average();
            result.RoundTripMs = resolveTimes.Average();
            result.PacketLossPercent = (double)failures / _config.DnsTestDomains.Count * 100;
        }
        else
        {
            result.IsSuccess = false;
            result.PacketLossPercent = 100;
        }

        result.Details = "System DNS (may be cached). " + string.Join(" | ", details);
        return result;
    }
}
