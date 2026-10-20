using System.Net;
using System.Net.NetworkInformation;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Implementations;

public sealed class ExternalProbe(ILogger<ExternalProbe> logger, ProbeConfiguration config,
    Func<string, int, CancellationToken, Task<double?>>? sendPing = null) : IProbe
{
    public ProbeType Type => ProbeType.External;
    public string Name => "External Ping";

    public async Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var batches = await Task.WhenAll(config.ExternalTargets.Select(async target =>
        {
            using var ping = new Ping();
            var times = new List<double>();
            for (int i = 0; i < config.PingsPerCycle; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    double? ms;
                    if (sendPing != null) ms = await sendPing(target, config.IcmpTimeoutMs, cancellationToken);
                    else
                    {
                        var reply = await ping.SendPingAsync(IPAddress.Parse(target), TimeSpan.FromMilliseconds(config.IcmpTimeoutMs), cancellationToken: cancellationToken);
                        ms = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
                    }
                    if (ms.HasValue) times.Add(ms.Value);
                }
                catch (PingException ex) { logger.LogDebug(ex, "ICMP failure for {Target}", target); }
            }
            return (Target: target, Times: times);
        }));
        var all = batches.SelectMany(b => b.Times).ToList();
        var jitters = batches.Where(b => b.Times.Count >= 2).Select(b => Jitter(b.Times)).ToList();
        int sent = config.ExternalTargets.Count * config.PingsPerCycle;
        return new ProbeResult
        {
            ProbeType = Type, Target = string.Join(", ", config.ExternalTargets),
            ConnectionType = NetworkHelper.GetConnectionType(), AdapterName = NetworkHelper.GetActiveAdapterName(),
            IsSuccess = all.Count > 0, RoundTripMs = all.Count > 0 ? all.Average() : null,
            JitterMs = jitters.Count > 0 ? jitters.Average() : null, PacketLossPercent = 100d * (sent - all.Count) / sent,
            Details = string.Join(" | ", batches.Select(b => $"{b.Target}: avg={(b.Times.Count > 0 ? b.Times.Average().ToString("F1") : "unknown")}ms, loss={config.PingsPerCycle - b.Times.Count}/{config.PingsPerCycle}"))
        };
    }

    internal static double Jitter(IReadOnlyList<double> values) => Enumerable.Range(1, values.Count - 1).Average(i => Math.Abs(values[i] - values[i-1]));
}
