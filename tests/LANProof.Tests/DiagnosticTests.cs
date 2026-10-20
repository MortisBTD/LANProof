using LANProof.Core.Diagnostics;
using LANProof.Core.Models;

namespace LANProof.Tests;

public sealed class DiagnosticTests
{
    private static readonly DateTime Time = new(2026, 10, 21, 1, 0, 0, DateTimeKind.Utc);
    private static ProbeResult P(ProbeType type, bool success, string device = "pc", int second = 0) => new()
    { ProbeType = type, IsSuccess = success, Timestamp = Time.AddSeconds(second), DeviceId = device, Target = type.ToString(), PacketLossPercent = success ? 0 : 100 };
    private static DiagnosticReport Analyze(params ProbeResult[] data) => new CorrelationEngine().Analyze([], data, Time, Time.AddMinutes(1));

    [Fact] public void EmptyPeriodDoesNotClaimHealthyConnection()
    {
        var report = Analyze();
        Assert.Null(report.SuccessRatePercent); Assert.Null(report.ProbableLayer); Assert.Contains("Нет измерений", report.Summary);
    }
    [Fact] public void HealthyGatewayAndFailedInternetLocateWanAsHypothesis()
    {
        var report = Analyze(P(ProbeType.Gateway, true), P(ProbeType.External, false), P(ProbeType.Http, false));
        Assert.Equal(NetworkLayer.Internet, report.ProbableLayer); Assert.InRange(report.ConfidencePercent, 1, 55);
        Assert.Contains("не доказывают", report.Recommendations[0]);
    }
    [Fact] public void WorkingHttpsPreventsClaimingInternetOutageWhenIcmpFails()
    {
        var report = Analyze(P(ProbeType.Gateway, true), P(ProbeType.External, false), P(ProbeType.Http, true));
        Assert.Contains("ICMP", report.Summary); Assert.Contains("HTTPS работает", report.Summary);
    }
    [Fact] public void GatewayFailureWithWorkingInternetIsNotLocalOutage()
    {
        var report = Analyze(P(ProbeType.Gateway, false), P(ProbeType.External, true), P(ProbeType.Http, true));
        Assert.Contains("интернет доступен", report.Summary);
    }
    [Fact] public void DnsFailureWithWorkingIpLocatesDns()
    {
        var report = Analyze(P(ProbeType.External, true), P(ProbeType.Dns, false));
        Assert.Equal(NetworkLayer.Dns, report.ProbableLayer);
    }
    [Fact] public void DifferentDevicesCannotBeCombinedIntoWanEvidence()
    {
        var report = Analyze(P(ProbeType.Gateway, true, "pc"), P(ProbeType.External, false, "phone"), P(ProbeType.Http, false, "phone"));
        Assert.Null(report.ProbableLayer);
    }
    [Fact] public void DifferentCyclesCannotBeCombinedIntoWanEvidence()
    {
        var report = Analyze(P(ProbeType.Gateway, true), P(ProbeType.External, false, second: 5), P(ProbeType.Http, false, second: 5));
        Assert.Null(report.ProbableLayer);
    }
    [Fact] public void OutOfPeriodDataCannotAffectDiagnosis()
    {
        var report = Analyze(P(ProbeType.Gateway, true), P(ProbeType.External, false, second: 70));
        Assert.Single(report.SupportingMeasurements); Assert.Equal(100, report.SuccessRatePercent);
    }
    [Fact] public void HighLatencyIsNotReportedAsCompletelyHealthy()
    {
        var probe = P(ProbeType.External, true); probe.RoundTripMs = 500;
        Assert.Contains("отклонения", Analyze(probe).Summary);
    }
    [Fact] public void EventsDeduplicateAndResolveOnlySameDeviceAndTarget()
    {
        var detector = new EventDetector(new());
        var detected = new List<NetworkEvent>(); var resolved = new List<NetworkEvent>();
        detector.EventDetected += (_, e) => detected.Add(e); detector.EventResolved += (_, e) => resolved.Add(e);
        detector.ProcessResult(P(ProbeType.Gateway, false)); detector.ProcessResult(P(ProbeType.Gateway, false, second: 5));
        Assert.Single(detected);
        detector.ProcessResult(P(ProbeType.Gateway, true, "phone", 10)); Assert.Empty(resolved);
        var otherTarget = P(ProbeType.Gateway, true, second: 10); otherTarget.Target = "other";
        detector.ProcessResult(otherTarget); Assert.Empty(resolved);
        detector.ProcessResult(P(ProbeType.Gateway, true, second: 15));
        Assert.Single(resolved); Assert.Equal(Time.AddSeconds(15), resolved[0].EndTime); Assert.Empty(detector.GetActiveEvents());
    }
    [Fact] public void ConfigurationRejectsEmptyTargetsAndInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new ProbeConfiguration { ExternalTargets = [] }.Validate());
        Assert.Throws<ArgumentException>(() => new ProbeConfiguration { HttpTargets = ["http://example.com"] }.Validate());
        Assert.Throws<ArgumentException>(() => new ProbeConfiguration { IntervalSeconds = 0 }.Validate());
    }
}
