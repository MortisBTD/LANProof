using LANProof.Core.Interfaces;
using LANProof.Core.Models;

namespace LANProof.Core.Diagnostics;

public sealed class EventDetector(ProbeConfiguration config) : IEventDetector
{
    private readonly Dictionary<(string, ProbeType, string, EventCategory), NetworkEvent> _active = [];
    public event EventHandler<NetworkEvent>? EventDetected;
    public event EventHandler<NetworkEvent>? EventResolved;
    public IReadOnlyList<NetworkEvent> GetActiveEvents() => _active.Values.ToList();

    public void ProcessResult(ProbeResult result)
    {
        if (!result.IsConclusive) return; // Missing observations neither diagnose an outage nor prove recovery.
        var issues = new List<(EventCategory Category, EventSeverity Severity, string Title)>();
        if (!result.IsSuccess)
            issues.Add((result.ProbeType == ProbeType.Dns ? EventCategory.DnsIssue : EventCategory.ConnectionLost,
                EventSeverity.Critical, $"{Label(result.ProbeType)}: проверка не прошла"));
        else
        {
            if (result.PacketLossPercent >= config.PacketLossWarningPercent)
                issues.Add((EventCategory.PacketLoss, result.PacketLossPercent >= config.PacketLossCriticalPercent ? EventSeverity.Critical : EventSeverity.Warning,
                    $"{Label(result.ProbeType)}: неудачных проверок {result.PacketLossPercent:F0}%"));
            if (result.ProbeType is ProbeType.Gateway or ProbeType.External && result.RoundTripMs >= config.LatencyWarningMs)
                issues.Add((EventCategory.LatencySpike, result.RoundTripMs >= config.LatencyCriticalMs ? EventSeverity.Critical : EventSeverity.Warning,
                    $"{Label(result.ProbeType)}: задержка {result.RoundTripMs:F0} мс"));
            if (result.ProbeType is ProbeType.Gateway or ProbeType.External && result.JitterMs >= config.JitterWarningMs)
                issues.Add((EventCategory.HighJitter, EventSeverity.Warning, $"{Label(result.ProbeType)}: высокий джиттер"));
            if (result.SignalStrengthDbm <= config.WiFiSignalWarningDbm)
                issues.Add((EventCategory.WiFiSignalDrop, EventSeverity.Warning, "Слабый сигнал Wi-Fi (оценка)"));
        }

        foreach (var key in _active.Keys.Where(k => k.Item1 == result.DeviceId && k.Item2 == result.ProbeType && k.Item3 == result.Target).ToList())
        {
            if (issues.Any(i => i.Category == key.Item4)) continue;
            var resolved = _active[key];
            resolved.EndTime = result.Timestamp;
            _active.Remove(key);
            EventResolved?.Invoke(this, resolved);
        }
        foreach (var issue in issues)
        {
            var key = (result.DeviceId, result.ProbeType, result.Target, issue.Category);
            if (_active.ContainsKey(key)) continue;
            var networkEvent = new NetworkEvent
            {
                StartTime = result.Timestamp, Category = issue.Category, Severity = issue.Severity,
                Layer = Layer(result.ProbeType), Title = issue.Title,
                Description = $"Цель: {result.Target}. {result.Details}", DeviceId = result.DeviceId,
                ConnectionType = result.ConnectionType
            };
            _active.Add(key, networkEvent);
            EventDetected?.Invoke(this, networkEvent);
        }
    }

    public static NetworkLayer Layer(ProbeType type) => type switch
    {
        ProbeType.Adapter => NetworkLayer.Device, ProbeType.Gateway => NetworkLayer.LocalNetwork,
        ProbeType.External => NetworkLayer.Internet, ProbeType.Dns => NetworkLayer.Dns, _ => NetworkLayer.Service
    };
    public static string Label(ProbeType type) => type switch
    {
        ProbeType.Adapter => "Устройство", ProbeType.Gateway => "Шлюз", ProbeType.External => "Внешние IP",
        ProbeType.Dns => "DNS", _ => "Сервисы HTTPS"
    };
}
