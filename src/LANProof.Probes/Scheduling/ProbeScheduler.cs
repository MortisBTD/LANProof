using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Scheduling;

public sealed class ProbeScheduler(IEnumerable<IProbe> probes, ILogger<ProbeScheduler> logger, ProbeConfiguration config) : IProbeScheduler, IDisposable
{
    private readonly IReadOnlyList<IProbe> _probes = probes.ToList();
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    public async Task<IReadOnlyList<ProbeResult>> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        config.Validate();
        await _cycleGate.WaitAsync(cancellationToken);
        try
        {
            var timestamp = DateTime.UtcNow;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(config.CycleTimeoutMs);
            return await Task.WhenAll(_probes.Select(async probe =>
            {
                try
                {
                    var result = await probe.ExecuteAsync(deadline.Token).WaitAsync(deadline.Token);
                    result.Timestamp = timestamp;
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Probe {ProbeName} failed", probe.Name);
                    return new ProbeResult { ProbeType = probe.Type, Timestamp = timestamp, IsSuccess = false, IsConclusive = false,
                        Target = probe.Name, Details = ex is OperationCanceledException ? "Cycle deadline exceeded; result unavailable. This is not a measured packet-loss percentage." : $"Probe error: {ex.Message}" };
                }
            }));
        }
        finally { _cycleGate.Release(); }
    }
    public void Dispose() => _cycleGate.Dispose();
}
