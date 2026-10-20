using LANProof.Core.Models;

namespace LANProof.Core.Diagnostics;

public sealed record CrossDeviceFinding(DateTime Timestamp,string Summary,string Evidence,int MatchingCycles);

/// <summary>Matches original per-device cycles with bounded clock uncertainty. Never infers health from a gap.</summary>
public sealed class MultiDeviceAccumulator
{
    private sealed record Snapshot(DateTime Time,string Device,string Gateway,bool Wifi,bool AdapterBad,bool GatewayGood,bool GatewayBad,bool InternetGood,bool WanBad,double? Uncertainty);
    private readonly SortedDictionary<long,List<Snapshot>> _buckets=[];
    private readonly Dictionary<string,SortedDictionary<DateTime,CrossDeviceFinding>> _findings=[];
    public void AddCycle(IReadOnlyList<ProbeResult> results)
    {
        foreach(var group in results.GroupBy(p=>(p.Timestamp,p.DeviceId)))
        {
            var data=group.ToList();
            bool Good(ProbeType type)=>data.Any(p=>p.ProbeType==type && p.IsConclusive && p.IsSuccess && p.PacketLossPercent is null or 0);
            bool Bad(ProbeType type)=>data.Any(p=>p.ProbeType==type && p.IsConclusive && !p.IsSuccess);
            var snapshot=new Snapshot(group.Key.Timestamp,group.Key.DeviceId,data.FirstOrDefault(p=>p.ProbeType==ProbeType.Gateway)?.Target ?? "",
                data.Any(p=>p.ConnectionType==ConnectionType.WiFi),Bad(ProbeType.Adapter),Good(ProbeType.Gateway),Bad(ProbeType.Gateway),
                Good(ProbeType.External)&&Good(ProbeType.Http),Bad(ProbeType.External)&&Bad(ProbeType.Http),
                data.Any(p=>p.ClockUncertaintyMs==null) ? null : data.Max(p=>p.ClockUncertaintyMs));
            if(snapshot.Uncertainty is not (>=0 and <=1000)) continue;
            long key=snapshot.Time.Ticks/(3*TimeSpan.TicksPerSecond);
            for(long bin=key-1;bin<=key+1;bin++)
            {
                if(!_buckets.TryGetValue(bin,out var peers))continue;
                foreach(var peer in peers)
                {
                    if(peer.Device==snapshot.Device || peer.Device.StartsWith("android:")==snapshot.Device.StartsWith("android:"))continue;
                    var phone=snapshot.Device.StartsWith("android:") ? snapshot : peer;
                    var pc=ReferenceEquals(phone,snapshot) ? peer : snapshot;
                    var delta=Math.Abs((phone.Time-pc.Time).TotalMilliseconds);
                    if(delta>2500 || delta+phone.Uncertainty+pc.Uncertainty>3000)continue;
                    string? summary=null;string evidence="";
                    if(phone.Wifi && (phone.AdapterBad || phone.GatewayBad) && pc.GatewayGood && pc.InternetGood)
                    {summary="Вероятная проблема Wi-Fi или Android-устройства";evidence="Телефон потерял Wi-Fi/доступ к шлюзу, а ПК сохранил доступ к шлюзу и внешним сервисам.";}
                    else if(phone.GatewayGood && pc.GatewayGood && phone.WanBad && pc.WanBad && phone.Gateway==pc.Gateway)
                    {summary="Одновременное отклонение на выходе общей сети";evidence="На обоих устройствах один шлюз доступен, внешние IP и HTTPS не прошли. Это не доказывает вину провайдера.";}
                    if(summary==null)continue;
                    if(!_findings.TryGetValue(summary,out var matches))_findings[summary]=matches=[];
                    matches[phone.Time]=new(phone.Time,summary,evidence+$" Расхождение циклов {delta:F0} мс; погрешность ПК ±{pc.Uncertainty:F0}, Android ±{phone.Uncertainty:F0} мс.",1);
                }
            }
            if(!_buckets.TryGetValue(key,out var bucket))_buckets[key]=bucket=[];
            bucket.Add(snapshot);
        }
    }
    public void TrimBefore(DateTime start)
    {
        long bin=start.Ticks/(3*TimeSpan.TicksPerSecond)-1;
        while(_buckets.Count>0 && _buckets.First().Key<bin)_buckets.Remove(_buckets.First().Key);
        foreach(var matches in _findings.Values)while(matches.Count>0 && matches.First().Key<start)matches.Remove(matches.First().Key);
    }
    public List<CrossDeviceFinding> Findings()=>_findings.Values.Where(m=>m.Count>0).Select(m=>m.Last().Value with {MatchingCycles=m.Count}).OrderByDescending(f=>f.Timestamp).ToList();
}
