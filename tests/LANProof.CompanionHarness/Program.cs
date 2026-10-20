using System.Net;
using System.Text.Json;
using LANProof.Runtime;
using LANProof.Runtime.Companion;

// Test-only loopback peer. Auto-approval is deliberately absent from the production desktop application.
if(args.Length!=1)throw new ArgumentException("Pass an isolated interop output directory");
string directory=Path.GetFullPath(args[0]);Directory.CreateDirectory(directory);
var session=new MonitoringSession(Path.Combine(directory,"database"));await session.InitializeAsync();
await using var host=new CompanionHost(session);int requested=0;
host.Changed+=()=>
{
    if(host.PendingName!=null && Interlocked.Exchange(ref requested,1)==0)
        _=Task.Run(async()=>await host.ApproveAsync());
};
session.Updated+=()=>_=Task.Run(async()=>
{
    var rows=await session.ReadAsync(DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddMinutes(1));
    await File.WriteAllTextAsync(Path.Combine(directory,"imported-count.txt"),rows.Measurements.Count.ToString());
});
host.Start(IPAddress.Loopback,0);var invitation=host.CreateInvitation();
await File.WriteAllTextAsync(Path.Combine(directory,"server.json"),JsonSerializer.Serialize(invitation,CompanionWire.Json));
Console.WriteLine("Loopback companion harness ready");await Task.Delay(TimeSpan.FromMinutes(10));
