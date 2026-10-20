namespace LANProof.Core.Models;

/// <summary>
/// A diagnostic report combining events, measurements and a conclusion.
/// </summary>
public sealed class DiagnosticReport
{
    /// <summary>Unique identifier.</summary>
    public long Id { get; set; }

    /// <summary>When the report was generated (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Start of the analysis period.</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>End of the analysis period.</summary>
    public DateTime PeriodEnd { get; set; }

    public int TotalMeasurements { get; set; }
    public int InconclusiveMeasurements { get; set; }
    public double? SuccessRatePercent { get; set; }
    /// <summary>Arithmetic mean of observed ICMP batch losses; service/DNS checks never dilute it.</summary>
    public double? IcmpPacketLossPercent { get; set; }
    public double? GatewayPacketLossPercent { get; set; }
    public double? ExternalPacketLossPercent { get; set; }
    public bool MeasurementsAreSampled { get; set; }
    public int TotalEvents { get; set; }
    public bool HasAnomalies { get; set; }
    public List<ProbeSummary> ProbeSummaries { get; set; } = [];

    /// <summary>The most likely problem area.</summary>
    public NetworkLayer? ProbableLayer { get; set; }

    /// <summary>Confidence in the diagnosis (0-100).</summary>
    public int ConfidencePercent { get; set; }

    /// <summary>Human-readable diagnosis summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Detailed explanation with supporting evidence.</summary>
    public string DetailedAnalysis { get; set; } = string.Empty;

    /// <summary>Recommended next steps for the user.</summary>
    public List<string> Recommendations { get; set; } = [];

    /// <summary>Events that were analyzed.</summary>
    public List<NetworkEvent> RelatedEvents { get; set; } = [];

    /// <summary>Key measurements supporting the diagnosis.</summary>
    public List<ProbeResult> SupportingMeasurements { get; set; } = [];
}

public sealed record ProbeSummary(ProbeType ProbeType, int Count, int Failures);
