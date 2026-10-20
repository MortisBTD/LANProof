using LANProof.Core.Diagnostics;
using LANProof.Core.Models;
using LANProof.Data;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Runtime;

/// <summary>Loads a period once, then reads only IDs committed since the previous refresh.</summary>
public sealed class HistoryWindow
{
    private readonly MonitoringSession _session;
    private readonly DiagnosticAccumulator _analysis;
    private readonly MultiDeviceAccumulator _multi=new();
    private readonly long _bucketTicks;
    private readonly Dictionary<(long, string, ProbeType), Bucket> _buckets=[];
    private readonly SortedDictionary<(DateTime,long),ProbeResult> _tail=[];
    public long LastId { get; private set; }
    public long TotalRowsRead { get; private set; }
    public int LastRowsRead { get; private set; }
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }
    public List<ProbeResult> Preview { get; private set; }=[];
    public List<NetworkEvent> Events { get; private set; }=[];
    public DiagnosticReport Report { get; private set; }=new();
    private sealed class Bucket
    {
        public ProbeResult? First, Last, Min, Max, Worst;
        public void Add(ProbeResult p)
        {
            if (First==null || p.Timestamp<First.Timestamp || (p.Timestamp==First.Timestamp && p.Id<First.Id)) First=p;
            if (Last==null || p.Timestamp>Last.Timestamp || (p.Timestamp==Last.Timestamp && p.Id>Last.Id)) Last=p;
            if (p.IsSuccess && p.IsConclusive && p.RoundTripMs.HasValue)
            {
                if (Min==null || p.RoundTripMs<Min.RoundTripMs) Min=p;
                if (Max==null || p.RoundTripMs>Max.RoundTripMs) Max=p;
            }
            double Rank(ProbeResult r)=>!r.IsConclusive ? 101 : !r.IsSuccess ? 200 : r.PacketLossPercent ?? 0;
            if (Worst==null || Rank(p)>Rank(Worst)) Worst=p;
        }
        public IEnumerable<ProbeResult> Samples()=>new[] {First,Last,Min,Max,Worst}.OfType<ProbeResult>();
    }
    private HistoryWindow(MonitoringSession session, DateTime start, DateTime end)
    {
        _session=session; _analysis=new(session.Configuration);
        _bucketTicks=Math.Max(TimeSpan.TicksPerSecond,(end-start).Ticks/300); Start=start; End=end;
    }
    public static async Task<HistoryWindow> LoadAsync(MonitoringSession session, DateTime start, DateTime end, CancellationToken ct=default)
    {
        var window=new HistoryWindow(session,start,end);
        await window.RefreshAsync(start,end,ct); return window;
    }
    public async Task RefreshAsync(DateTime start, DateTime end, CancellationToken ct=default)
    {
        if (end<start || start<Start) throw new ArgumentException("Для расширения периода создайте новое окно истории.");
        Start=start; End=end; LastRowsRead=0;
        await using var db=LanProofDbContextFactory.Create(_session.DatabasePath);
        long throughId=await db.ProbeResults.MaxAsync(p=>(long?)p.Id,ct) ?? 0;
        var cycle=new List<ProbeResult>();
        await foreach (var p in _session.StreamMeasurementsAsync(start,end,LastId,throughId,ct))
        {
            LastRowsRead++; TotalRowsRead++;
            if (cycle.Count>0 && cycle[0].Timestamp!=p.Timestamp) { _analysis.AddCycle(cycle);_multi.AddCycle(cycle); cycle.Clear(); }
            cycle.Add(p);
            _tail[(p.Timestamp,p.Id)]=p;
            if (_tail.Count>200) _tail.Remove(_tail.First().Key);
            if (p.ProbeType is ProbeType.Gateway or ProbeType.External)
            {
                var key=(p.Timestamp.Ticks/_bucketTicks,p.DeviceId,p.ProbeType);
                if (!_buckets.TryGetValue(key,out var bucket)) _buckets[key]=bucket=new();
                bucket.Add(p);
            }
        }
        if (cycle.Count>0) {_analysis.AddCycle(cycle);_multi.AddCycle(cycle);}
        LastId=throughId;
        _analysis.TrimBefore(start);
        _multi.TrimBefore(start);
        foreach (var key in _buckets.Keys.Where(k=>(k.Item1+1)*_bucketTicks<start.Ticks).ToList()) _buckets.Remove(key);
        foreach (var key in _tail.Keys.TakeWhile(k=>k.Item1<start).ToList()) _tail.Remove(key);
        Preview=_buckets.Values.SelectMany(b=>b.Samples()).Concat(_tail.Values).Where(p=>p.Timestamp>=start && p.Timestamp<=end)
            .DistinctBy(p=>p.Id).OrderBy(p=>p.Timestamp).ThenBy(p=>p.Id).ToList();
        var query=db.NetworkEvents.AsNoTracking().Where(e=>e.StartTime<=end && (e.EndTime==null || e.EndTime>=start));
        int count=await query.CountAsync(ct);
        Events=await query.OrderByDescending(e=>e.StartTime).Take(1500).ToListAsync(ct);
        Report=_analysis.BuildReport(start,end,Preview,Events,count);
        Report.CrossDeviceFindings=_multi.Findings();
    }
}
