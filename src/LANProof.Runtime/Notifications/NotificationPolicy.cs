using LANProof.Core.Models;

namespace LANProof.Runtime.Notifications;

public enum AlertKind { Onset, Recovery, StillFailing }
public sealed record NetworkAlert(AlertKind Kind, NetworkEvent Source, string Title, string Body);

/// <summary>Consumes committed cycle snapshots on one thread. Targets share a device/category episode.</summary>
public sealed class NotificationPolicy
{
    private sealed class Episode(NetworkEvent source)
    {
        public NetworkEvent Source { get; } = source;
        public bool Announced { get; set; }
        public bool Escalated { get; set; }
    }
    private readonly Dictionary<(string, EventCategory), Episode> _active = [];
    private readonly Dictionary<(string, EventCategory), DateTime> _lastOnset = [];
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan EscalationAfter { get; init; } = TimeSpan.FromMinutes(3);

    // Pause/exit interrupts observation; it must never manufacture a recovery.
    public void Suspend() => _active.Clear();

    public IReadOnlyList<NetworkAlert> Evaluate(IReadOnlyList<NetworkEvent> active, DateTime now)
    {
        var groups = active.Where(e => e.Severity != EventSeverity.Info && e.EndTime == null)
            .GroupBy(e => (e.DeviceId, e.Category)).ToDictionary(g => g.Key, g => g.MinBy(e => e.StartTime)!);
        var alerts = new List<NetworkAlert>();
        foreach (var key in _active.Keys.Except(groups.Keys).ToArray())
        {
            var episode = _active[key];
            if (episode.Announced)
                alerts.Add(new(AlertKind.Recovery, episode.Source, "Проверки восстановились",
                    $"{key.Item1} · {key.Item2}. Проблема больше не наблюдается. Длительность: {Spell(now - episode.Source.StartTime)}."));
            _active.Remove(key);
        }
        foreach (var (key, source) in groups)
        {
            if (!_active.TryGetValue(key, out var episode)) _active[key] = episode = new(source);
            if (!episode.Announced)
            {
                if (_lastOnset.TryGetValue(key, out var last) && now - last < RepeatWindow) continue;
                episode.Announced = true;
                _lastOnset[key] = now;
                alerts.Add(new(AlertKind.Onset, source, source.Title, $"{source.DeviceId} · {source.Description}"));
            }
            else if (!episode.Escalated && now - episode.Source.StartTime >= EscalationAfter)
            {
                episode.Escalated = true;
                alerts.Add(new(AlertKind.StillFailing, source, "Проблема сохраняется",
                    $"{source.DeviceId} · {source.Title}. Длится {Spell(now - episode.Source.StartTime)}."));
            }
        }
        return alerts;
    }

    private static string Spell(TimeSpan span) => span < TimeSpan.FromMinutes(1)
        ? $"{Math.Max(1, (int)span.TotalSeconds)} с"
        : span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes} мин"
        : $"{(int)span.TotalHours} ч {span.Minutes} мин";
}
