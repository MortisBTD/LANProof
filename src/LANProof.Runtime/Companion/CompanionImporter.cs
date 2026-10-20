using LANProof.Core.Models;
using LANProof.Core.Diagnostics;
using LANProof.Data;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Runtime.Companion;

public sealed class CompanionImporter(MonitoringSession session)
{
    public async Task<long> ImportAsync(string deviceId,IReadOnlyList<CompanionCycle> cycles,CancellationToken ct=default)
    {
        if(cycles.Count is <1 or >50) throw new InvalidDataException("Batch must contain 1–50 cycles");
        var now=DateTimeOffset.UtcNow;
        foreach(var cycle in cycles)
        {
            var timestamp=DateTimeOffset.FromUnixTimeMilliseconds(cycle.TimestampUtcMs);
            if(cycle.Sequence<=0 || timestamp<now.AddDays(-30) || timestamp>now.AddMinutes(5)
                || cycle.ClockUncertaintyMs is <0 || (cycle.ClockUncertaintyMs.HasValue && !double.IsFinite(cycle.ClockUncertaintyMs.Value))
                || cycle.Observations==null || cycle.Observations.Count!=5 || cycle.Observations.Select(p=>p.ProbeType).Distinct().Count()!=5)
                throw new InvalidDataException("Invalid or expired cycle");
            foreach(var p in cycle.Observations)
            {
                if(!Enum.TryParse<ProbeType>(p.ProbeType,out var type) || !Enum.IsDefined(type) || p.Target==null || p.Target.Length>1024 || p.Details?.Length>4096
                    || new[]{p.RoundTripMs,p.JitterMs,p.DnsResolveMs,p.PacketLossPercent}.Any(v=>v.HasValue && (!double.IsFinite(v.Value)||v<0))
                    || p.PacketLossPercent>100 || p.HttpStatusCode is <100 or >599)
                    throw new InvalidDataException("Invalid observation");
            }
        }
        await using var db=LanProofDbContextFactory.Create(session.DatabasePath);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        foreach(var cycle in cycles.OrderBy(c=>c.Sequence))
        {
            int inserted=await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR IGNORE INTO CompanionReceipts (DeviceId,Sequence,TimestampUtcMs) VALUES ({deviceId},{cycle.Sequence},{cycle.TimestampUtcMs})",ct);
            if(inserted==0) continue;
            foreach(var p in cycle.Observations)
            {
                var type=Enum.Parse<ProbeType>(p.ProbeType);
                var result=new ProbeResult {Timestamp=DateTimeOffset.FromUnixTimeMilliseconds(cycle.TimestampUtcMs).UtcDateTime,
                    DeviceId=deviceId,ConnectionType=ConnectionType.WiFi,AdapterName="Android Wi-Fi",ProbeType=type,Target=p.Target,
                    IsSuccess=p.IsSuccess,IsConclusive=p.IsConclusive,RoundTripMs=p.RoundTripMs,JitterMs=p.JitterMs,
                    PacketLossPercent=p.PacketLossPercent,DnsResolveMs=p.DnsResolveMs,HttpStatusCode=p.HttpStatusCode,
                    ClockUncertaintyMs=cycle.ClockUncertaintyMs,Details=p.Details};
                db.ProbeResults.Add(result);
                if(p.IsConclusive && (!p.IsSuccess || p.PacketLossPercent>0))
                    db.NetworkEvents.Add(new NetworkEvent {DeviceId=deviceId,StartTime=result.Timestamp,EndTime=result.Timestamp,
                        ConnectionType=ConnectionType.WiFi,Layer=EventDetector.Layer(type),Severity=p.IsSuccess ? EventSeverity.Warning : EventSeverity.Critical,
                        Category=p.IsSuccess ? EventCategory.PacketLoss : EventCategory.ConnectionLost,
                        Title="Android · "+EventDetector.Label(type)+": отклонение",Description=p.Details ?? "Сохранённое наблюдение телефона; не подтверждает длительность обрыва."});
            }
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);session.NotifyImportedMeasurements();
        return cycles.Max(c=>c.Sequence);
    }
}
