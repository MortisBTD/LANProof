using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using LANProof.Runtime;

namespace LANProof.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LANProof-session-" + Guid.NewGuid().ToString("N"));
    private sealed class Probe(bool success) : IProbe
    {
        public ProbeType Type => ProbeType.Gateway;
        public string Name => "test gateway";
        public Task<ProbeResult> ExecuteAsync(CancellationToken ct = default) => Task.FromResult(new ProbeResult
        { ProbeType = Type, Target = "192.0.2.1", IsSuccess = success, PacketLossPercent = success ? 0 : 100 });
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact] public async Task MeasurementsAndRecoveryPersistAcrossCycles()
    {
        int cycle = 0;
        var session = new MonitoringSession(_directory, _ => [new Probe(cycle++ > 0)]);
        await session.InitializeAsync();
        await session.MeasureAsync(); await session.MeasureAsync();
        var (measurements, events) = await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Equal(2, measurements.Count); Assert.Single(events); Assert.NotNull(events[0].EndTime);
        Assert.DoesNotContain("не подтверждено", events[0].Description);
        await session.StopAsync();
    }
    [Fact] public async Task AlertSnapshotsAreCommittedAndDetachedFromDetector()
    {
        int cycle=0; var session=new MonitoringSession(_directory,_=>[new Probe(cycle++>0)]);
        await session.InitializeAsync();
        var snapshots=new List<IReadOnlyList<NetworkEvent>>();
        session.AlertsUpdated += snapshot=>snapshots.Add(snapshot);
        await session.MeasureAsync();
        var original=Assert.Single(Assert.Single(snapshots));
        Assert.True(original.Id>0); Assert.Null(original.EndTime);
        await session.MeasureAsync();
        Assert.Empty(snapshots[1]); Assert.Null(original.EndTime);
        await session.StopAsync(); Assert.Empty(session.ActiveEvents);
    }
    [Fact] public async Task StoppingDoesNotInventARecovery()
    {
        var session = new MonitoringSession(_directory, _ => [new Probe(false)]);
        await session.InitializeAsync(); await session.MeasureAsync(); await session.StopAsync();
        var (measurements, events) = await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Single(measurements); Assert.Single(events);
        Assert.Equal(measurements[0].Timestamp, events[0].EndTime);
        Assert.Contains("восстановление не подтверждено", events[0].Description);
    }
    [Fact] public async Task RestartClosesOrphanedObservationAtLastKnownSample()
    {
        var first = new MonitoringSession(_directory, _ => [new Probe(false)]);
        await first.InitializeAsync(); await first.MeasureAsync();
        var restarted = new MonitoringSession(_directory, _ => [new Probe(true)]);
        await restarted.InitializeAsync();
        var (_, events) = await restarted.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Single(events); Assert.NotNull(events[0].EndTime);
        Assert.Contains("Наблюдение прервано", events[0].Description);
    }
    [Fact] public async Task SavingSettingsPersistsAndClosesPreviousTargetObservation()
    {
        var session = new MonitoringSession(_directory, _ => [new Probe(false)]);
        await session.InitializeAsync(); await session.MeasureAsync();
        await session.SaveConfigurationAsync(new() { IntervalSeconds = 12, ExternalTargets = ["9.9.9.9"] });
        var reloaded = new MonitoringSession(_directory);
        Assert.Equal(12, reloaded.Configuration.IntervalSeconds); Assert.Equal("9.9.9.9", reloaded.Configuration.ExternalTargets[0]);
        var (_, events) = await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.NotNull(Assert.Single(events).EndTime);
    }
    [Fact] public async Task StartStopStartLeavesNoOverlappingLoop()
    {
        var session = new MonitoringSession(_directory, _ => [new Probe(true)]);
        await session.InitializeAsync();
        session.Start();
        async Task WaitForCount(int required)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var (data, _) = await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
                if (data.Count >= required) return;
                await Task.Delay(10);
            }
            Assert.Fail("Monitoring cycle did not persist within the test deadline.");
        }
        await WaitForCount(1);
        await session.StopAsync(); Assert.False(session.IsRunning);
        session.Start(); await WaitForCount(2); await session.StopAsync();
        var (measurements, _) = await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.True(measurements.Count >= 2); Assert.False(session.IsRunning);
    }
}
