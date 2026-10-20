using System.Text.Json;
using LANProof.Core.Models;
using LANProof.Data;
using LANProof.Data.Repositories;
using LANProof.Export;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Tests;

public sealed class StorageAndExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LANProof-tests-" + Guid.NewGuid().ToString("N"));
    public StorageAndExportTests() => Directory.CreateDirectory(_directory);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }

    [Fact] public async Task SqliteRoundTripPreservesUtcAndLatestObservations()
    {
        var path = Path.Combine(_directory, "history;test.db");
        var time = DateTime.UtcNow;
        await using (var db = LanProofDbContextFactory.Create(path))
        {
            var repo = new ProbeResultRepository(db);
            await repo.AddRangeAsync([
                new() { Timestamp = time, ProbeType = ProbeType.Gateway, IsSuccess = false },
                new() { Timestamp = time.AddSeconds(5), ProbeType = ProbeType.Gateway, IsSuccess = true },
                new() { Timestamp = time, ProbeType = ProbeType.Dns, IsSuccess = true }]);
        }
        await using var reopened = LanProofDbContextFactory.Create(path);
        var results = await new ProbeResultRepository(reopened).GetLatestByTypeAsync();
        Assert.Equal(2, results.Count); Assert.True(results.Single(p => p.ProbeType == ProbeType.Gateway).IsSuccess);
        Assert.All(results, p => Assert.Equal(DateTimeKind.Utc, p.Timestamp.Kind));
    }
    [Fact] public async Task EventQueryIncludesOverlappingEventsAndRetentionKeepsActiveOnes()
    {
        await using var db = LanProofDbContextFactory.Create(Path.Combine(_directory, "events.db"));
        var now = DateTime.UtcNow; var repo = new NetworkEventRepository(db);
        await repo.AddAsync(new() { StartTime = now.AddDays(-40), EndTime = null, Title = "active" });
        await repo.AddAsync(new() { StartTime = now.AddHours(-2), EndTime = now.AddMinutes(-10), Title = "overlap" });
        await repo.AddAsync(new() { StartTime = now.AddDays(-40), EndTime = now.AddDays(-35), Title = "old" });
        Assert.Equal(2, (await repo.GetByTimeRangeAsync(now.AddHours(-1), now)).Count);
        Assert.Equal(1, await repo.DeleteOlderThanAsync(now.AddDays(-30)));
        Assert.Single(await repo.GetOngoingAsync());
    }
    [Fact] public async Task JsonAndCsvPreserveEvidenceAndEscapeSpreadsheetFormulas()
    {
        var report = new DiagnosticReport { Summary = "Проверка", SupportingMeasurements = [new()
        { DeviceId = "=1+1", Target = "a,\"b\"", Details = "line1\nline2", IsSuccess = true, RoundTripMs = 12.5 }] };
        var exporter = new ReportExportService();
        var json = await exporter.ExportToJsonAsync(report, Path.Combine(_directory, "report.json"));
        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(json));
        Assert.Equal("=1+1", parsed.RootElement.GetProperty("SupportingMeasurements")[0].GetProperty("DeviceId").GetString());
        var csv = await exporter.ExportToCsvAsync(report, Path.Combine(_directory, "report.csv"));
        var text = await File.ReadAllTextAsync(csv);
        Assert.Contains("\"'=1+1\"", text); Assert.Contains("\"a,\"\"b\"\"\"", text); Assert.Contains("12.5", text);
    }
    [Fact] public async Task PdfIsGeneratedWithRussianEvidence()
    {
        var report = new DiagnosticReport { Summary = "Вероятная проблема DNS", DetailedAnalysis = "Шлюз доступен; DNS не отвечает.",
            Recommendations = ["Проверить системный резолвер."], SupportingMeasurements = [new() { ProbeType = ProbeType.Dns, IsSuccess = false }] };
        var path = await new ReportExportService().ExportToPdfAsync(report, Path.Combine(_directory, "report.pdf"));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 1000); Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
