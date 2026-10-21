using LANProof.Core.Models;
using LANProof.Runtime.Notifications;

namespace LANProof.Tests;

public class NotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static NetworkEvent Loss(string title = "Потеря пакетов", string device = "PC",
        EventCategory category = EventCategory.PacketLoss) => new()
    {
        StartTime = Now, Severity = EventSeverity.Warning, Category = category,
        Layer = NetworkLayer.Internet, Title = title, Description = "Цель недоступна.", DeviceId = device
    };

    [Fact] public void FirstOutageAndEscalationAreAnnouncedOnce()
    {
        var policy = new NotificationPolicy(); var e = Loss();
        Assert.Equal(AlertKind.Onset, Assert.Single(policy.Evaluate([e], Now)).Kind);
        Assert.Empty(policy.Evaluate([e], Now.AddMinutes(2)));
        Assert.Equal(AlertKind.StillFailing, Assert.Single(policy.Evaluate([e], Now.AddMinutes(3))).Kind);
        Assert.Empty(policy.Evaluate([e], Now.AddMinutes(8)));
    }
    [Fact] public void RecoveryNeedsAnnouncedOutageAndOccursOnce()
    {
        var policy = new NotificationPolicy();
        Assert.Empty(policy.Evaluate([], Now));
        policy.Evaluate([Loss()], Now);
        var alert = Assert.Single(policy.Evaluate([], Now.AddMinutes(3)));
        Assert.Equal(AlertKind.Recovery, alert.Kind); Assert.Contains("3 мин", alert.Body);
        Assert.Empty(policy.Evaluate([], Now.AddMinutes(4)));
    }
    [Fact] public void CooldownIsMeasuredFromOutageRatherThanRecovery()
    {
        var policy = new NotificationPolicy();
        policy.Evaluate([Loss()], Now);
        policy.Evaluate([], Now.AddMinutes(9));
        Assert.Empty(policy.Evaluate([Loss()], Now.AddMinutes(9.5)));
        Assert.Equal(AlertKind.Onset, Assert.Single(policy.Evaluate([Loss()], Now.AddMinutes(10))).Kind);
    }
    [Fact] public void SuppressedEpisodeDoesNotAnnounceRecovery()
    {
        var policy = new NotificationPolicy();
        policy.Evaluate([Loss()], Now); policy.Evaluate([], Now.AddMinutes(1));
        Assert.Empty(policy.Evaluate([Loss()], Now.AddMinutes(2)));
        Assert.Empty(policy.Evaluate([], Now.AddMinutes(3)));
    }
    [Fact] public void DevicesAndCategoriesHaveIndependentSuppression()
    {
        var policy = new NotificationPolicy();
        var alerts = policy.Evaluate([Loss(), Loss(device:"phone"), Loss(category:EventCategory.DnsIssue)], Now);
        Assert.Equal(3, alerts.Count);
    }
    [Fact] public void OneRecoveredTargetDoesNotRecoverCategory()
    {
        var policy = new NotificationPolicy(); var a = Loss("a"); var b = Loss("b");
        Assert.Single(policy.Evaluate([a,b], Now));
        Assert.Empty(policy.Evaluate([b], Now.AddSeconds(20)));
        Assert.Equal(AlertKind.Recovery, Assert.Single(policy.Evaluate([], Now.AddSeconds(40))).Kind);
    }
    [Fact] public void InfoAndAlreadyEndedEventsStaySilent()
    {
        var policy = new NotificationPolicy(); var info = Loss(); info.Severity = EventSeverity.Info;
        var ended = Loss(); ended.EndTime = Now;
        Assert.Empty(policy.Evaluate([info,ended], Now));
    }
    [Fact] public void StoppingDoesNotManufactureRecovery()
    {
        var policy = new NotificationPolicy(); policy.Evaluate([Loss()], Now);
        policy.Suspend(); Assert.Empty(policy.Evaluate([], Now.AddMinutes(1)));
    }
}
