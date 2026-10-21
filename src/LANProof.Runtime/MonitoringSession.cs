using System.Text.Json;
using System.Runtime.CompilerServices;
using LANProof.Core.Diagnostics;
using LANProof.Core.Models;
using LANProof.Core.Interfaces;
using LANProof.Data;
using LANProof.Probes.Implementations;
using LANProof.Probes.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LANProof.Runtime;

public sealed class MonitoringSession
{
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public ProbeConfiguration Configuration { get; private set; } = new();
    public bool IsRunning => _loop is { IsCompleted: false };
    public event Action? Updated;
    public event EventHandler<NetworkEvent>? AlertRaised;
    public event Action<IReadOnlyList<NetworkEvent>>? AlertsUpdated;
    private IReadOnlyList<NetworkEvent> _activeEvents = Array.Empty<NetworkEvent>();
    public IReadOnlyList<NetworkEvent> ActiveEvents => Volatile.Read(ref _activeEvents);
    private static NetworkEvent Snapshot(NetworkEvent e) => new()
    {
        Id=e.Id, StartTime=e.StartTime, EndTime=e.EndTime, Severity=e.Severity, Category=e.Category,
        Layer=e.Layer, Title=e.Title, Description=e.Description, DeviceId=e.DeviceId, ConnectionType=e.ConnectionType
    };
    public void NotifyImportedMeasurements() => Updated?.Invoke();
    public string? Error { get; private set; }
    private Task? _loop;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EventDetector _detector;
    private readonly List<NetworkEvent> _changed = [];
    private readonly Func<ProbeConfiguration, IReadOnlyList<IProbe>> _createProbes;
    private readonly TimeProvider _clock;

    public MonitoringSession(string? directory = null, Func<ProbeConfiguration, IReadOnlyList<IProbe>>? createProbes = null, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _createProbes = createProbes ?? (config => [
            new AdapterProbe(NullLogger<AdapterProbe>.Instance),
            new GatewayProbe(NullLogger<GatewayProbe>.Instance, config),
            new ExternalProbe(NullLogger<ExternalProbe>.Instance, config),
            new DnsProbe(NullLogger<DnsProbe>.Instance, config),
            new HttpProbe(NullLogger<HttpProbe>.Instance, config)]);
        directory ??= ApplicationPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, "lanproof.db");
        SettingsPath = Path.Combine(directory, "settings.json");
        if (File.Exists(SettingsPath))
        {
            try
            {
                Configuration = JsonSerializer.Deserialize<ProbeConfiguration>(File.ReadAllText(SettingsPath)) ?? new();
                Configuration.Validate();
            }
            catch (Exception ex) { Configuration = new(); Error = "Настройки не прочитаны; применены стандартные: " + ex.Message; }
        }
        _detector = CreateDetector();
    }

    private EventDetector CreateDetector()
    {
        var detector = new EventDetector(Configuration);
        detector.EventDetected += (_, e) => Track(e);
        detector.EventResolved += (_, e) => Track(e);
        return detector;
    }

    private void Track(NetworkEvent e)
    {
        _changed.Add(e);

    }

    public async Task InitializeAsync()
    {
        await using var db = LanProofDbContextFactory.Create(DatabasePath);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        // End orphaned observations at the last known measurement, without claiming recovery.
        var active = await db.NetworkEvents.Where(e => e.EndTime == null).ToListAsync();
        var last = await db.ProbeResults.MaxAsync(p => (DateTime?)p.Timestamp);
        foreach (var e in active)
        {
            e.EndTime = last.HasValue && last >= e.StartTime ? last.Value : e.StartTime;
            e.Description += " Наблюдение прервано; восстановление не подтверждено.";
        }
        var threshold = DateTime.UtcNow.AddDays(-30);
        var receiptCutoff=new DateTimeOffset(threshold).ToUnixTimeMilliseconds();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM CompanionReceipts WHERE TimestampUtcMs < {receiptCutoff}");
        await db.ProbeResults.Where(p => p.Timestamp < threshold).ExecuteDeleteAsync();
        await db.NetworkEvents.Where(e => e.EndTime != null && e.EndTime < threshold).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
    }

    public void Start()
    {
        if (IsRunning) return;
        Configuration.Validate();
        _cts = new();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        Updated?.Invoke();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Configuration.IntervalSeconds), _clock);
            do
            {
                await MeasureAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Error = "Мониторинг остановлен: " + ex.Message; }
        finally { Updated?.Invoke(); }
    }

    public async Task StopAsync()
    {
        if (_cts != null) await _cts.CancelAsync();
        if (_loop != null) await _loop;
        _cts?.Dispose(); _cts = null; _loop = null;
        await EndObservationsAsync();
        Updated?.Invoke();
    }

    private async Task EndObservationsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = LanProofDbContextFactory.Create(DatabasePath);
            var last = await db.ProbeResults.MaxAsync(p => (DateTime?)p.Timestamp);
            foreach (var e in _detector.GetActiveEvents())
            {
                e.EndTime = last is { } time && time >= e.StartTime ? time : e.StartTime;
                e.Description += " Мониторинг остановлен; восстановление не подтверждено.";
                db.NetworkEvents.Update(e);
            }
            await db.SaveChangesAsync();
            _detector = CreateDetector();
            Volatile.Write(ref _activeEvents, Array.Empty<NetworkEvent>());
        }
        finally { _gate.Release(); }
    }

    public async Task MeasureAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        IReadOnlyList<IProbe> probes = [];
        try
        {
            Error = null;
            probes = _createProbes(Configuration);
            using var scheduler = new ProbeScheduler(probes, NullLogger<ProbeScheduler>.Instance, Configuration);
            var results = await scheduler.RunOnceAsync(ct);
            await using var db = LanProofDbContextFactory.Create(DatabasePath);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.ProbeResults.AddRange(results);
            _changed.Clear();
            foreach (var p in results) _detector.ProcessResult(p);
            foreach (var e in _changed.Distinct())
                if (e.Id == 0) db.NetworkEvents.Add(e); else db.NetworkEvents.Update(e);
            // Complete a started cycle atomically even if Stop was pressed just after probing.
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            var active = _detector.GetActiveEvents().Select(Snapshot).ToArray();
            Volatile.Write(ref _activeEvents, Array.AsReadOnly(active));
            // Subscribers receive detached values only after successful persistence.
            foreach (var e in _changed.Distinct()) AlertRaised?.Invoke(this, Snapshot(e));
            AlertsUpdated?.Invoke(ActiveEvents);
        }
        finally
        {
            foreach (var resource in probes.OfType<IDisposable>()) resource.Dispose();
            _gate.Release();
        }
        Updated?.Invoke();
    }

    public async Task<(List<ProbeResult> Measurements, List<NetworkEvent> Events)> ReadAsync(DateTime start, DateTime end)
    {
        await using var db = LanProofDbContextFactory.Create(DatabasePath);
        var measurements = await db.ProbeResults.AsNoTracking().Where(p => p.Timestamp >= start && p.Timestamp <= end).OrderBy(p => p.Timestamp).ThenBy(p=>p.Id).Take(5000).ToListAsync();
        var events = await db.NetworkEvents.AsNoTracking().Where(e => e.StartTime <= end && (e.EndTime == null || e.EndTime >= start)).OrderByDescending(e => e.StartTime).Take(1500).ToListAsync();
        return (measurements, events);
    }

    public async IAsyncEnumerable<ProbeResult> StreamMeasurementsAsync(DateTime start, DateTime end, long afterId=0,
        long throughId=long.MaxValue, [EnumeratorCancellation] CancellationToken ct=default)
    {
        await using var db=LanProofDbContextFactory.Create(DatabasePath);
        var query=db.ProbeResults.AsNoTracking().Where(p=>p.Id>afterId && p.Id<=throughId && p.Timestamp>=start && p.Timestamp<=end);
        var ordered=afterId>0 ? query.OrderBy(p=>p.Id) : query.OrderBy(p=>p.Timestamp).ThenBy(p=>p.Id);
        await foreach (var p in ordered.AsAsyncEnumerable().WithCancellation(ct)) yield return p;
    }

    public async IAsyncEnumerable<NetworkEvent> StreamEventsAsync(DateTime start, DateTime end, [EnumeratorCancellation] CancellationToken ct=default)
    {
        await using var db=LanProofDbContextFactory.Create(DatabasePath);
        await foreach (var e in db.NetworkEvents.AsNoTracking().Where(e=>e.StartTime<=end && (e.EndTime==null || e.EndTime>=start))
            .OrderBy(e=>e.StartTime).ThenBy(e=>e.Id).AsAsyncEnumerable().WithCancellation(ct)) yield return e;
    }

    public async Task<List<ProbeResult>> ReadCycleAsync(DateTime timestamp, CancellationToken ct=default)
    {
        await using var db=LanProofDbContextFactory.Create(DatabasePath);
        return await db.ProbeResults.AsNoTracking().Where(p=>p.Timestamp==timestamp).OrderBy(p=>p.Id).Take(100).ToListAsync(ct);
    }

    public async Task SaveConfigurationAsync(ProbeConfiguration configuration)
    {
        if (IsRunning) throw new InvalidOperationException("Остановите мониторинг перед изменением настроек.");
        configuration.Validate();
        await EndObservationsAsync();
        await _gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(SettingsPath + ".tmp", JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
            Configuration = configuration;
            _detector = CreateDetector();
        }
        finally { _gate.Release(); }
    }
}
