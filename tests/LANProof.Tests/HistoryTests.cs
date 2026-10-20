using System.Diagnostics;
using System.Text.Json;
using LANProof.Core.Diagnostics;
using LANProof.Core.Models;
using LANProof.Data;
using LANProof.Export;
using LANProof.Runtime;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"LANProof-history-"+Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start=new(2026,10,1,0,0,0,DateTimeKind.Utc);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(_directory))Directory.Delete(_directory,true); }

    private async Task<MonitoringSession> Seed(int rows)
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();
        await using var db=LanProofDbContextFactory.Create(session.DatabasePath);
        await db.Database.ExecuteSqlRawAsync("""
            WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x+1<{0})
            INSERT INTO ProbeResults (Timestamp,ProbeType,Target,IsSuccess,IsConclusive,ConnectionType,DeviceId,RoundTripMs,PacketLossPercent)
            SELECT datetime('2026-10-01','+'||(CAST(x/5 AS INTEGER)*5)||' seconds'),
              CASE x%5 WHEN 0 THEN 'Gateway' WHEN 1 THEN 'External' WHEN 2 THEN 'Dns' WHEN 3 THEN 'Http' ELSE 'Adapter' END,
              'fixture.local',CASE WHEN x%50000=1 THEN 0 ELSE 1 END,1,'Ethernet','synthetic-benchmark',
              CASE WHEN x%5=4 THEN NULL ELSE 10 END,CASE WHEN x%50000=1 THEN 100 ELSE 0 END FROM n;
            """,rows);
        return session;
    }

    [Fact] public async Task WeekOf604800MeasurementsIsSampledAndRefreshReadsOnlyNewRows()
    {
        var session=await Seed(604800);var end=Start.AddDays(7);
        var watch=Stopwatch.StartNew();var history=await HistoryWindow.LoadAsync(session,Start,end);
        long initialMs=watch.ElapsedMilliseconds;
        Assert.Equal(604800,history.Report.TotalMeasurements);Assert.Equal(604800,history.TotalRowsRead);
        Assert.InRange(history.Preview.Count,1,3300);Assert.True(history.Report.MeasurementsAreSampled);
        Assert.Contains(history.Preview,p=>!p.IsSuccess);
        await using(var db=LanProofDbContextFactory.Create(session.DatabasePath))
        {
            db.ProbeResults.AddRange(Enum.GetValues<ProbeType>().Select(t=>new ProbeResult {Timestamp=end,ProbeType=t,IsSuccess=true,PacketLossPercent=0}));
            await db.SaveChangesAsync();
        }
        watch.Restart();await history.RefreshAsync(Start.AddSeconds(5),end.AddSeconds(5));
        long incrementalMs=watch.ElapsedMilliseconds;
        Assert.Equal(5,history.LastRowsRead);Assert.Equal(604805,history.TotalRowsRead);Assert.Equal(604800,history.Report.TotalMeasurements);
        Assert.InRange(history.Preview.Count,1,3300);
        var destination=Environment.GetEnvironmentVariable("LANPROOF_BENCHMARK_DIR");
        if(!string.IsNullOrEmpty(destination))
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination,"week-history.json"),JsonSerializer.Serialize(new
            {SyntheticRows=604800,InitialLoadMs=initialMs,IncrementalRefreshMs=incrementalMs,NewRowsRead=history.LastRowsRead,PreviewPoints=history.Preview.Count,ManagedMemoryMB=Math.Round(GC.GetTotalMemory(true)/1048576d,1)},new JsonSerializerOptions{WriteIndented=true}));
        }
    }

    [Fact] public async Task BoundedPreviewDoesNotTruncateFullJsonOrCsvExports()
    {
        var session=await Seed(6005);var end=Start.AddHours(3);
        await using(var db=LanProofDbContextFactory.Create(session.DatabasePath))
        {
            db.NetworkEvents.AddRange(Enumerable.Range(0,1601).Select(i=>new NetworkEvent {StartTime=Start.AddSeconds(i),EndTime=Start.AddSeconds(i+1),Title="synthetic event"}));
            await db.SaveChangesAsync();
        }
        var history=await HistoryWindow.LoadAsync(session,Start,end);
        Assert.Equal(5000,(await session.ReadAsync(Start,end)).Measurements.Count);
        Assert.True(history.Preview.Count<6005);Assert.Equal(1500,history.Events.Count);Assert.Equal(1601,history.Report.TotalEvents);
        var exporter=new ReportExportService();
        var json=await exporter.ExportToJsonAsync(history.Report,session.StreamMeasurementsAsync(Start,end),session.StreamEventsAsync(Start,end),Path.Combine(_directory,"full.json"));
        using var parsed=JsonDocument.Parse(await File.ReadAllTextAsync(json));
        Assert.Equal(6005,parsed.RootElement.GetProperty("SupportingMeasurements").GetArrayLength());
        Assert.Equal(1601,parsed.RootElement.GetProperty("RelatedEvents").GetArrayLength());
        Assert.False(parsed.RootElement.GetProperty("MeasurementsAreSampled").GetBoolean());
        var csv=await exporter.ExportToCsvAsync(history.Report,session.StreamMeasurementsAsync(Start,end),session.StreamEventsAsync(Start,end),Path.Combine(_directory,"full.csv"));
        Assert.Equal(6005,File.ReadLines(csv).Count(line=>line.StartsWith("\"measurement\"",StringComparison.Ordinal)));
        Assert.Equal(1601,File.ReadLines(csv).Count(line=>line.StartsWith("\"event\"",StringComparison.Ordinal)));
        Assert.Contains("http_headers_ms",File.ReadLines(csv).First());
    }

    [Fact] public async Task CancelledStreamingExportPreservesExistingFile()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"existing.json");
        await File.WriteAllTextAsync(path,"previous report");using var cts=new CancellationTokenSource();
        async IAsyncEnumerable<ProbeResult> Interrupted()
        {
            yield return new ProbeResult();await Task.Yield();cts.Cancel();cts.Token.ThrowIfCancellationRequested();
        }
        async IAsyncEnumerable<NetworkEvent> Empty() { await Task.CompletedTask;yield break; }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ReportExportService().ExportToJsonAsync(new(),Interrupted(),Empty(),path,cts.Token));
        Assert.Equal("previous report",await File.ReadAllTextAsync(path));Assert.Empty(Directory.GetFiles(_directory,"*.tmp"));
    }

    [Fact] public async Task LegacyDatabaseUpgradePreservesRowsAndRemovesOnlyEmptyReportTable()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"legacy.db");
        await using(var old=new LanProofDbContext(LanProofDbContextFactory.CreateOptions(path)))
        {
            await old.Database.EnsureCreatedAsync();
            await old.Database.ExecuteSqlRawAsync("ALTER TABLE ProbeResults DROP COLUMN IsConclusive");
            await old.Database.ExecuteSqlRawAsync("CREATE TABLE DiagnosticReports (Id INTEGER PRIMARY KEY)");
            await old.Database.ExecuteSqlRawAsync("INSERT INTO ProbeResults (Timestamp,ProbeType,Target,IsSuccess,ConnectionType,DeviceId) VALUES ('2026-10-01 00:00:00','Gateway','192.0.2.1',1,'Ethernet','legacy')");
        }
        await using var upgraded=LanProofDbContextFactory.Create(path);
        Assert.True((await upgraded.ProbeResults.SingleAsync()).IsConclusive);
        await upgraded.Database.OpenConnectionAsync();using var cmd=upgraded.Database.GetDbConnection().CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DiagnosticReports'";
        Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }

    [Fact] public void RollingAnalysisMatchesFullAnalysisAfterOldCyclesExpire()
    {
        var data=Enumerable.Range(0,100).SelectMany(i=>new[]
        {
            new ProbeResult {Timestamp=Start.AddSeconds(i*5),ProbeType=ProbeType.Gateway,IsSuccess=true,PacketLossPercent=0},
            new ProbeResult {Timestamp=Start.AddSeconds(i*5),ProbeType=ProbeType.External,IsSuccess=i%4!=0,PacketLossPercent=i%4==0 ? 100 : 0},
            new ProbeResult {Timestamp=Start.AddSeconds(i*5),ProbeType=ProbeType.Http,IsSuccess=i%4!=0,PacketLossPercent=i%4==0 ? 100 : 0}
        }).ToList();
        var accumulator=new DiagnosticAccumulator();foreach(var cycle in data.GroupBy(p=>p.Timestamp))accumulator.AddCycle(cycle.ToList());
        var start=Start.AddSeconds(300);var end=Start.AddSeconds(500);accumulator.TrimBefore(start);
        var rolling=accumulator.BuildReport(start,end);var full=new CorrelationEngine().Analyze([],data,start,end);
        Assert.Equal(full.TotalMeasurements,rolling.TotalMeasurements);Assert.Equal(full.SuccessRatePercent,rolling.SuccessRatePercent);
        Assert.Equal(full.GatewayPacketLossPercent,rolling.GatewayPacketLossPercent);Assert.Equal(full.ExternalPacketLossPercent,rolling.ExternalPacketLossPercent);
        Assert.Equal(full.Summary,rolling.Summary);Assert.Equal(full.ConfidencePercent,rolling.ConfidencePercent);
    }
}
