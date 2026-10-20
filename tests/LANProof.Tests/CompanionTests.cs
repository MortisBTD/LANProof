using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using LANProof.Core.Diagnostics;
using LANProof.Core.Models;
using LANProof.Runtime;
using LANProof.Runtime.Companion;

namespace LANProof.Tests;

public sealed class CompanionTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"LANProof-companion-"+Guid.NewGuid().ToString("N"));
    private readonly string _id=Guid.NewGuid().ToString();
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private CompanionRequest Request(string action,string credential="",List<CompanionCycle>? cycles=null)=>new(1,action,_id,"Test Android",credential,cycles);
    private static async Task<CompanionResponse> Exchange(PairingInvitation invite,CompanionRequest request,string? pin=null)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));using var client=new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(invite.Host),invite.Port,deadline.Token);
        using var tls=new SslStream(client.GetStream(),false,(_,cert,_,_)=>cert!=null &&
            Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(pin??invite.Fingerprint,StringComparison.OrdinalIgnoreCase));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {TargetHost="LANProof Companion",EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},deadline.Token);
        await CompanionWire.WriteAsync(tls,request,deadline.Token);return await CompanionWire.ReadAsync<CompanionResponse>(tls,deadline.Token);
    }
    private static CompanionCycle Cycle(long sequence=1)=>new(sequence,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),20,
        Enum.GetValues<ProbeType>().Select(t=>new CompanionObservation(t.ToString(),"fixture",true,true,10,0,t is ProbeType.Gateway or ProbeType.External ? 0 : null)).ToList());

    [Fact] public async Task TlsPairingRequiresDesktopApprovalThenDeduplicatesRetriedOfflineBatch()
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();
        await using var host=new CompanionHost(session);host.Start(IPAddress.Loopback,0);var invite=host.CreateInvitation();
        CompanionResponse first;
        try {first=await Exchange(invite,Request("hello"));}catch(Exception ex){throw new InvalidOperationException(host.Error??"No server diagnostic",ex);}
        Assert.Equal("unauthorized",first.Status);
        Assert.Equal("pending",(await Exchange(invite,Request("pair",invite.Secret))).Status);Assert.Equal("Test Android",host.PendingName);
        Assert.Equal("unauthorized",(await Exchange(invite,Request("sync",invite.Secret,[Cycle()]))).Status);
        await host.ApproveAsync();var paired=await Exchange(invite,Request("pair",invite.Secret));Assert.Equal("paired",paired.Status);Assert.NotNull(paired.Token);
        var request=Request("sync",paired.Token!,[Cycle()]);
        Assert.Equal(1,(await Exchange(invite,request)).AcknowledgedThrough);Assert.Equal(1,(await Exchange(invite,request)).AcknowledgedThrough);
        var rows=(await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddMinutes(1))).Measurements;
        Assert.Equal(5,rows.Count);Assert.All(rows,p=>Assert.Equal("android:"+_id,p.DeviceId));
        await host.RevokeAsync();Assert.Equal("unauthorized",(await Exchange(invite,Request("hello",paired.Token!))).Status);
    }
    [Fact] public async Task WrongCertificateFingerprintCannotConnect()
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();
        await using var host=new CompanionHost(session);host.Start(IPAddress.Loopback,0);var invite=host.CreateInvitation();
        await Assert.ThrowsAnyAsync<AuthenticationException>(()=>Exchange(invite,Request("hello"),new string('0',64)));
        Assert.True(host.IsRunning);
    }
    [Fact] public async Task ServerIdentityAndApprovedDeviceSurviveRestart()
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();string fingerprint,token;
        await using(var host=new CompanionHost(session))
        {
            host.Start(IPAddress.Loopback,0);var invite=host.CreateInvitation();fingerprint=invite.Fingerprint;
            await Exchange(invite,Request("pair",invite.Secret));await host.ApproveAsync();token=(await Exchange(invite,Request("pair",invite.Secret))).Token!;
        }
        await using var restored=new CompanionHost(session);restored.Start(IPAddress.Loopback,0);var next=restored.CreateInvitation();
        Assert.Equal(fingerprint,next.Fingerprint);Assert.Equal("ok",(await Exchange(next,Request("hello",token))).Status);
    }
    [Fact] public async Task InvalidBatchIsAtomicAndCanBeRetriedAfterValidationFailure()
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();var importer=new CompanionImporter(session);
        var good=Cycle();var bad=Cycle(2) with {Observations=[new("Fake","target",true,true)]};
        await Assert.ThrowsAsync<InvalidDataException>(()=>importer.ImportAsync("android:"+_id,[good,bad]));
        Assert.Empty((await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddMinutes(1))).Measurements);
        await importer.ImportAsync("android:"+_id,[good]);Assert.Equal(5,(await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddMinutes(1))).Measurements.Count);
    }
    [Theory] [InlineData(-1)] [InlineData(256*1024+1)]
    public async Task FrameSizeIsBoundedBeforePayloadAllocation(int size)
    {
        var bytes=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes,size);
        await Assert.ThrowsAsync<InvalidDataException>(()=>CompanionWire.ReadAsync<CompanionRequest>(new MemoryStream(bytes)));
    }
    private static List<ProbeResult> Observations(string device,DateTime time,bool phoneBad,double? clock=0)=>Enum.GetValues<ProbeType>().Select(t=>new ProbeResult
    {DeviceId=device,Timestamp=time,ProbeType=t,Target=t==ProbeType.Gateway ? "192.168.1.1" : "fixture",ConnectionType=device.StartsWith("android:") ? ConnectionType.WiFi : ConnectionType.Ethernet,
        IsSuccess=!(phoneBad && t is ProbeType.Gateway or ProbeType.Adapter),PacketLossPercent=phoneBad && t==ProbeType.Gateway ? 100 : 0,ClockUncertaintyMs=clock}).ToList();
    [Fact] public void CorrelationRequiresSameTimeAndTrustedClockAndDoesNotMixDeviceProbes()
    {
        var now=DateTime.UtcNow;var phone=Observations("android:phone",now.AddMilliseconds(100),true,20);var pc=Observations("pc",now,false);
        var report=new CorrelationEngine().Analyze([],pc.Concat(phone).ToList(),now.AddMinutes(-1),now.AddMinutes(1));
        Assert.Single(report.CrossDeviceFindings);Assert.Contains("Wi-Fi",report.CrossDeviceFindings[0].Summary);
        foreach(var p in phone)p.ClockUncertaintyMs=null;
        Assert.Empty(new CorrelationEngine().Analyze([],pc.Concat(phone).ToList(),now.AddMinutes(-1),now.AddMinutes(1)).CrossDeviceFindings);
        foreach(var p in phone){p.ClockUncertaintyMs=10;p.Timestamp=now.AddSeconds(10);}
        Assert.Empty(new CorrelationEngine().Analyze([],pc.Concat(phone).ToList(),now.AddMinutes(-1),now.AddMinutes(1)).CrossDeviceFindings);
    }
    [Fact] public async Task LateOfflineRowsUpdateHistoryAndCrossDeviceAnalysisWithoutFullReload()
    {
        var session=new MonitoringSession(_directory);await session.InitializeAsync();var now=DateTime.UtcNow.AddSeconds(-10);
        await using(var db=LANProof.Data.LanProofDbContextFactory.Create(session.DatabasePath)){db.ProbeResults.AddRange(Observations(Environment.MachineName,now,false));await db.SaveChangesAsync();}
        var history=await HistoryWindow.LoadAsync(session,now.AddMinutes(-1),now.AddMinutes(1));Assert.Equal(5,history.LastRowsRead);
        var cycle=Cycle() with {TimestampUtcMs=new DateTimeOffset(now).ToUnixTimeMilliseconds(),Observations=Enum.GetValues<ProbeType>().Select(t=>new CompanionObservation(t.ToString(),t==ProbeType.Gateway ? "192.168.1.1" : "fixture",t is not (ProbeType.Gateway or ProbeType.Adapter),true,PacketLossPercent:t==ProbeType.Gateway ? 100 : 0)).ToList()};
        await new CompanionImporter(session).ImportAsync("android:"+_id,[cycle]);await history.RefreshAsync(now.AddMinutes(-1),now.AddMinutes(1));
        Assert.Equal(5,history.LastRowsRead);Assert.Equal(10,history.Report.TotalMeasurements);Assert.Single(history.Report.CrossDeviceFindings);
    }
    [Fact] public async Task JointFindingAndClockUncertaintyReachJsonCsvAndPdf()
    {
        Directory.CreateDirectory(_directory);var now=DateTime.UtcNow;
        var data=Observations("pc",now,false).Concat(Observations("android:fixture",now,true,20)).ToList();
        var report=new CorrelationEngine().Analyze([],data,now.AddMinutes(-1),now.AddMinutes(1));
        Assert.Single(report.CrossDeviceFindings);
        var exporter=new LANProof.Export.ReportExportService();
        var json=await exporter.ExportToJsonAsync(report,Path.Combine(_directory,"joint.json"));
        using var parsed=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(json));
        Assert.Single(parsed.RootElement.GetProperty("CrossDeviceFindings").EnumerateArray());
        var csv=await exporter.ExportToCsvAsync(report,Path.Combine(_directory,"joint.csv"));
        Assert.Contains("clock_uncertainty_ms",File.ReadLines(csv).First());Assert.Contains("\"cross_device\"",await File.ReadAllTextAsync(csv));
        var pdf=await exporter.ExportToPdfAsync(report,Path.Combine(_directory,"joint.pdf"));Assert.True(new FileInfo(pdf).Length>1000);
        var qa=Environment.GetEnvironmentVariable("LANPROOF_PDF_QA_DIR");
        if(!string.IsNullOrEmpty(qa)){Directory.CreateDirectory(qa);File.Copy(pdf,Path.Combine(qa,"joint-synthetic.pdf"),true);}
    }
}
