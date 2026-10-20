using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LANProof.Core.Diagnostics;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using LANProof.Probes.Implementations;
using LANProof.Probes.Scheduling;
using LANProof.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LANProof.Tests;

public sealed class OperationalTests
{
    private sealed class HeadUnsupportedHandler : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Methods.Add(request.Method);
            if(request.Method==HttpMethod.Get) Assert.Equal("bytes=0-0",request.Headers.Range!.ToString());
            return Task.FromResult(new HttpResponseMessage(request.Method==HttpMethod.Head ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.PartialContent));
        }
    }
    [Fact] public async Task UnsupportedHeadFallsBackToBoundedGetInsteadOfReportingAnOutage()
    {
        var handler=new HeadUnsupportedHandler();
        using var probe=new HttpProbe(NullLogger<HttpProbe>.Instance,new(){HttpTargets=["https://fixture.local"]},handler);
        var result=await probe.ExecuteAsync();
        Assert.True(result.IsSuccess);Assert.Equal(new[]{HttpMethod.Head,HttpMethod.Get},handler.Methods);
        Assert.Contains("HEAD + GET fallback",result.Details);
    }

    [Fact] public void MissingObservationDoesNotResolveAnExistingOutage()
    {
        var detector=new EventDetector(new());
        detector.ProcessResult(new(){ProbeType=ProbeType.Gateway,IsSuccess=false,Target="router"});
        var outage=Assert.Single(detector.GetActiveEvents());
        detector.ProcessResult(new(){ProbeType=ProbeType.Gateway,IsSuccess=false,IsConclusive=false,Target="router"});
        Assert.Same(outage,Assert.Single(detector.GetActiveEvents()));Assert.Null(outage.EndTime);
    }

    [Fact] public async Task FailedExternalTargetsRunConcurrentlyAndDoNotMixTargetJitter()
    {
        int active=0,maximum=0,sent=0;
        var probe=new ExternalProbe(NullLogger<ExternalProbe>.Instance,new(),async (_,timeout,ct)=>
        {
            Assert.Equal(800,timeout);Interlocked.Increment(ref sent);
            var count=Interlocked.Increment(ref active);int previous;
            do { previous=maximum; if (previous>=count) break; } while (Interlocked.CompareExchange(ref maximum,count,previous)!=previous);
            await Task.Delay(15,ct);Interlocked.Decrement(ref active);return null;
        });
        var result=await probe.ExecuteAsync();
        Assert.Equal(12,sent);Assert.Equal(3,maximum);Assert.False(result.IsSuccess);Assert.Equal(100,result.PacketLossPercent);
        var jitter=new ExternalProbe(NullLogger<ExternalProbe>.Instance,new() { PingsPerCycle=2,ExternalTargets=["1.1.1.1","8.8.8.8"] },
            (ip,_,_)=>Task.FromResult<double?>(ip=="1.1.1.1" ? 10 : 100));
        Assert.Equal(0,(await jitter.ExecuteAsync()).JitterMs);
    }

    private sealed class WaitingProbe : IProbe
    {
        public ProbeType Type=>ProbeType.External;public string Name=>"wait";
        public async Task<ProbeResult> ExecuteAsync(CancellationToken ct=default) { await Task.Delay(Timeout.Infinite,ct);return new(); }
    }
    [Fact] public async Task CycleCeilingReturnsUnknownWithoutInventingLossOrOutage()
    {
        using var runner=new ProbeScheduler([new WaitingProbe()],NullLogger<ProbeScheduler>.Instance,
            new() { IntervalSeconds=1,IcmpTimeoutMs=100,TimeoutMs=100,PingsPerCycle=1,CycleTimeoutMs=150 });
        var elapsed=Stopwatch.StartNew();var result=Assert.Single(await runner.RunOnceAsync());
        Assert.InRange(elapsed.ElapsedMilliseconds,50,2000);Assert.False(result.IsConclusive);Assert.Null(result.PacketLossPercent);
        var detector=new EventDetector(new());detector.ProcessResult(result);Assert.Empty(detector.GetActiveEvents());
        var report=new CorrelationEngine().Analyze([], [result],result.Timestamp,result.Timestamp.AddSeconds(1));
        Assert.Null(report.ProbableLayer);Assert.Null(report.SuccessRatePercent);Assert.Equal(1,report.InconclusiveMeasurements);
    }
    [Fact] public void PacketLossIsNotDilutedBySuccessfulDnsHttpAndAdapterChecks()
    {
        var time=DateTime.UtcNow;
        var data=Enum.GetValues<ProbeType>().Select(type=>new ProbeResult {Timestamp=time,ProbeType=type,IsSuccess=true,
            PacketLossPercent=type==ProbeType.Gateway ? 50 : type==ProbeType.External ? 0 : null}).ToList();
        var report=new CorrelationEngine().Analyze([],data,time,time.AddSeconds(1));
        Assert.Equal(100,report.SuccessRatePercent);Assert.Equal(50,report.GatewayPacketLossPercent);
        Assert.Equal(0,report.ExternalPacketLossPercent);Assert.Equal(25,report.IcmpPacketLossPercent);Assert.True(report.HasAnomalies);
    }

    [Fact] public async Task NewHttpProbeInstancesReuseTheSameRealTcpConnection()
    {
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        int connections=0,requests=0;
        var handlers=new ConcurrentBag<Task>();
        var server=Task.Run(async ()=>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var socket=await listener.AcceptTcpClientAsync(cts.Token);Interlocked.Increment(ref connections);
                    handlers.Add(Task.Run(async ()=>
                    {
                        using (socket)
                        using (var stream=socket.GetStream())
                        using (var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true))
                        try
                        {
                            while (!cts.IsCancellationRequested)
                            {
                                var line=await reader.ReadLineAsync(cts.Token);if (line==null) break;
                                Assert.StartsWith("HEAD ",line);Interlocked.Increment(ref requests);
                                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cts.Token))) { }
                                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"),cts.Token);
                            }
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                    }));
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (SocketException) when (cts.IsCancellationRequested) { }
        });
        try
        {
            var uri=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            for (int i=0;i<2;i++)
            {
                using var probe=new HttpProbe(NullLogger<HttpProbe>.Instance,new() { HttpTargets=[uri] });
                var result=await probe.ExecuteAsync(cts.Token);Assert.True(result.IsSuccess);Assert.Contains("not ICMP RTT",result.Details);
            }
            Assert.Equal(2,requests);Assert.Equal(1,connections);
        }
        finally { cts.Cancel();listener.Stop();await server;await Task.WhenAll(handlers); }
    }

    private sealed class TimedProbe(FakeTimeProvider clock,List<DateTimeOffset> starts) : IProbe
    {
        public ProbeType Type=>ProbeType.External;public string Name=>"timed";
        public async Task<ProbeResult> ExecuteAsync(CancellationToken ct=default)
        {
            lock(starts) starts.Add(clock.GetUtcNow());
            await Task.Delay(TimeSpan.FromSeconds(3),clock,ct);
            return new() {ProbeType=Type,IsSuccess=false,PacketLossPercent=100};
        }
    }
    [Fact] public async Task ProductionLoopUsesStartToStartCadenceDuringFailures()
    {
        var directory=Path.Combine(Path.GetTempPath(),"LANProof-clock-"+Guid.NewGuid().ToString("N"));
        var clock=new FakeTimeProvider();var starts=new List<DateTimeOffset>();
        var session=new MonitoringSession(directory,_=>[new TimedProbe(clock,starts)],clock);
        try
        {
            await session.InitializeAsync();session.Start();
            async Task Until(Func<bool> ready)
            { for(int i=0;i<300 && !ready();i++) await Task.Delay(10);Assert.True(ready()); }
            await Until(()=>starts.Count>=1);clock.Advance(TimeSpan.FromSeconds(3));
            for(int i=0;i<300;i++)
            {
                if ((await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow)).Measurements.Count>0) break;
                await Task.Delay(10);
            }
            clock.Advance(TimeSpan.FromSeconds(2));await Until(()=>starts.Count>=2);
            Assert.Equal(TimeSpan.FromSeconds(5),starts[1]-starts[0]);
        }
        finally { await session.StopAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true); }
    }
}
