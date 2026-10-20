namespace LANProof.Core.Models;

/// <summary>
/// Configuration for probe scheduling and targets.
/// </summary>
public sealed class ProbeConfiguration
{
    /// <summary>Start-to-start sampling interval in seconds.</summary>
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Number of pings per probe cycle (for averaging).</summary>
    public int PingsPerCycle { get; set; } = 4;

    /// <summary>Timeout for each probe in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 3000;
    public int IcmpTimeoutMs { get; set; } = 800;
    public int CycleTimeoutMs { get; set; } = 4500;

    /// <summary>External IP addresses to ping.</summary>
    public List<string> ExternalTargets { get; set; } =
    [
        "1.1.1.1",      // Cloudflare
        "8.8.8.8",      // Google
        "208.67.222.222" // OpenDNS
    ];

    /// <summary>Domains to resolve for DNS tests.</summary>
    public List<string> DnsTestDomains { get; set; } =
    [
        "google.com",
        "cloudflare.com",
        "github.com"
    ];

    /// <summary>HTTP endpoints to check availability.</summary>
    public List<string> HttpTargets { get; set; } =
    [
        "https://www.google.com",
        "https://1.1.1.1",
        "https://www.cloudflare.com"
    ];

    /// <summary>Latency threshold for warning (ms).</summary>
    public double LatencyWarningMs { get; set; } = 100;

    /// <summary>Latency threshold for critical (ms).</summary>
    public double LatencyCriticalMs { get; set; } = 300;

    /// <summary>Packet loss threshold for warning (%).</summary>
    public double PacketLossWarningPercent { get; set; } = 5;

    /// <summary>Packet loss threshold for critical (%).</summary>
    public double PacketLossCriticalPercent { get; set; } = 20;

    /// <summary>Jitter threshold for warning (ms).</summary>
    public double JitterWarningMs { get; set; } = 30;

    /// <summary>Wi-Fi signal warning threshold (dBm).</summary>
    public int WiFiSignalWarningDbm { get; set; } = -70;

    /// <summary>Wi-Fi signal critical threshold (dBm).</summary>
    public int WiFiSignalCriticalDbm { get; set; } = -80;

    public void Validate()
    {
        if (IntervalSeconds is < 1 or > 3600 || TimeoutMs is < 100 or > 30000 || PingsPerCycle is < 1 or > 10)
            throw new ArgumentException("Интервал: 1–3600 с; таймаут: 100–30000 мс; ping за цикл: 1–10.");
        if (ExternalTargets is not { Count: > 0 and <= 10 } || ExternalTargets.Any(x => !System.Net.IPAddress.TryParse(x, out _)))
            throw new ArgumentException("Укажите 1–10 внешних IP-адресов.");
        if (IcmpTimeoutMs is < 100 or > 30000 || CycleTimeoutMs is < 100 or > 120000 || CycleTimeoutMs > IntervalSeconds * 1000
            || (long)IcmpTimeoutMs * PingsPerCycle > CycleTimeoutMs || TimeoutMs > CycleTimeoutMs)
            throw new ArgumentException("Лимит цикла должен вмещать партию ICMP и таймаут DNS/HTTPS и не превышать интервал запуска. Уменьшите число ping/таймауты или увеличьте интервал.");
        if (DnsTestDomains is not { Count: > 0 and <= 10 } || DnsTestDomains.Any(x => Uri.CheckHostName(x) != UriHostNameType.Dns))
            throw new ArgumentException("Укажите 1–10 корректных доменных имён DNS.");
        if (HttpTargets is not { Count: > 0 and <= 10 } || HttpTargets.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("Укажите 1–10 HTTPS-адресов без логина и пароля.");
        if (LatencyWarningMs <= 0 || LatencyCriticalMs < LatencyWarningMs || PacketLossWarningPercent is <= 0 or > 100 || PacketLossCriticalPercent < PacketLossWarningPercent || PacketLossCriticalPercent > 100 || JitterWarningMs <= 0)
            throw new ArgumentException("Некорректные пороги задержки или потерь.");
    }
}
