using LANProof.Core.Interfaces;
using LANProof.Core.Models;

namespace LANProof.Core.Diagnostics;

public sealed class CorrelationEngine : ICorrelationEngine
{
    public DiagnosticReport Analyze(IReadOnlyList<NetworkEvent> events, IReadOnlyList<ProbeResult> measurements, DateTime periodStart, DateTime periodEnd)
    {
        if (periodEnd<periodStart) throw new ArgumentException("Конец периода раньше начала.");
        var data=measurements.Where(p=>p.Timestamp>=periodStart && p.Timestamp<=periodEnd).OrderBy(p=>p.Timestamp).ToList();
        var accumulator=new DiagnosticAccumulator();
        var multi=new MultiDeviceAccumulator();
        foreach (var cycle in data.GroupBy(p=>(p.DeviceId,p.Timestamp))) {var rows=cycle.ToList();accumulator.AddCycle(rows);multi.AddCycle(rows);}
        var report=accumulator.BuildReport(periodStart,periodEnd,data,
            events.Where(e=>e.StartTime<=periodEnd && (e.EndTime==null || e.EndTime>=periodStart)).ToList());
        report.CrossDeviceFindings=multi.Findings();return report;
    }
}
