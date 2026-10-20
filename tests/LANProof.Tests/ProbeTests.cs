using System.Net;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using LANProof.Probes.Implementations;
using LANProof.Probes.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;

namespace LANProof.Tests;

public sealed class ProbeTests
{
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(ct); }
    [Theory] [InlineData(200, true)] [InlineData(503, false)] [InlineData(404, false)]
    public async Task HttpStatusIsNotMistakenForServiceSuccess(int status, bool success)
    {
        using var probe = new HttpProbe(NullLogger<HttpProbe>.Instance, new() { HttpTargets = ["https://example.com"] },
            new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var result = await probe.ExecuteAsync();
        Assert.Equal(success, result.IsSuccess); Assert.Equal(success ? 0 : 100, result.PacketLossPercent);
    }
    [Fact] public async Task HttpTimeoutBecomesFailedObservation()
    {
        using var probe = new HttpProbe(NullLogger<HttpProbe>.Instance, new() { TimeoutMs = 100, HttpTargets = ["https://example.com"] },
            new Handler(async ct => { await Task.Delay(5000, ct); return new(HttpStatusCode.OK); }));
        Assert.False((await probe.ExecuteAsync()).IsSuccess);
    }
    [Fact] public async Task DnsTimeoutBecomesFailedObservation()
    {
        var probe = new DnsProbe(NullLogger<DnsProbe>.Instance, new() { TimeoutMs = 100, DnsTestDomains = ["example.com"] },
            async (_, ct) => { await Task.Delay(5000, ct); return [IPAddress.Loopback]; });
        Assert.False((await probe.ExecuteAsync()).IsSuccess);
    }
    [Fact] public async Task UserCancellationIsNotRecordedAsDnsFailure()
    {
        using var cts = new CancellationTokenSource();
        var probe = new DnsProbe(NullLogger<DnsProbe>.Instance, new() { DnsTestDomains = ["example.com"] },
            async (_, ct) => { cts.Cancel(); await Task.Delay(5000, ct); return []; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ExecuteAsync(cts.Token));
    }
    private sealed class FakeProbe(Func<Task<ProbeResult>> run) : IProbe
    {
        public ProbeType Type => ProbeType.External; public string Name => "fake";
        public Task<ProbeResult> ExecuteAsync(CancellationToken ct = default) => run();
    }
    [Fact] public async Task SchedulerPreservesProbeExceptionsAsEvidence()
    {
        using var scheduler = new ProbeScheduler([new FakeProbe(() => throw new IOException("network error"))], NullLogger<ProbeScheduler>.Instance, new());
        var results = await scheduler.RunOnceAsync();
        Assert.Single(results); Assert.False(results[0].IsSuccess); Assert.Contains("network error", results[0].Details);
    }
    [Fact] public async Task ConcurrentCyclesNeverOverlap()
    {
        int active = 0, maximum = 0;
        using var scheduler = new ProbeScheduler([new FakeProbe(async () =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref active));
            await Task.Delay(30); Interlocked.Decrement(ref active);
            return new() { ProbeType = ProbeType.External, IsSuccess = true };
        })], NullLogger<ProbeScheduler>.Instance, new());
        await Task.WhenAll(scheduler.RunOnceAsync(), scheduler.RunOnceAsync());
        Assert.Equal(1, maximum);
    }
}
