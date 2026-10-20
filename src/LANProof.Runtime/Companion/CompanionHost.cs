using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace LANProof.Runtime.Companion;

public sealed class CompanionHost : IAsyncDisposable
{
    private readonly CompanionIdentity _identity;
    private readonly CompanionImporter _importer;
    private readonly SemaphoreSlim _requests=new(4),_mutation=new(1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long,Task> _clients=[];
    private CancellationTokenSource? _cts;private TcpListener? _listener;private Task? _loop;private long _clientId;
    private PairingInvitation? _invitation;private (string Id,string Name,string Secret)? _pending;
    private string? _approvedPendingId;
    public PairedCompanion? Device=>_identity.Device;
    public string? PendingName=>_pending?.Name;
    public string? Error {get;private set;}
    public DateTime? LastSyncUtc {get;private set;}
    public bool IsRunning=>_loop is {IsCompleted:false};
    public event Action? Changed;
    public CompanionHost(MonitoringSession session)
    { _identity=new(Path.GetDirectoryName(session.DatabasePath)!);_importer=new(session); }
    public static IReadOnlyList<IPAddress> LocalAddresses()=>NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .OrderByDescending(n=>n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
        .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(a=>a.AddressFamily==AddressFamily.InterNetwork && IsPrivate(a)).Distinct().ToList();
    private static bool IsPrivate(IPAddress address)
    {var b=address.GetAddressBytes();return b[0]==10 || b[0]==172 && b[1] is >=16 and <=31 || b[0]==192 && b[1]==168;}
    public void Start(IPAddress address,int port=13791)
    {
        if(IsRunning) throw new InvalidOperationException("Уже запущено");
        if(!IPAddress.IsLoopback(address) && !LocalAddresses().Contains(address)) throw new ArgumentException("Выберите локальный IPv4");
        _listener=new(address,port);_listener.Start(8);_cts=new();_loop=ListenAsync(_cts.Token);Changed?.Invoke();
    }
    public PairingInvitation CreateInvitation()
    {
        if(_listener==null || !IsRunning) throw new InvalidOperationException("Сначала включите соединение");
        var endpoint=(IPEndPoint)_listener.LocalEndpoint;
        _invitation=new(1,endpoint.Address.ToString(),endpoint.Port,_identity.Fingerprint,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
        _pending=null;_approvedPendingId=null;Changed?.Invoke();return _invitation;
    }
    public async Task ApproveAsync()
    {
        await _mutation.WaitAsync();try
        {
            if(_pending is not {} pending || _invitation is not {} invitation || invitation.ExpiresUtcMs<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new InvalidOperationException("Запрос отсутствует или истёк");
            if(_approvedPendingId==pending.Id)return;
            _identity.Pair(new(pending.Id,pending.Name,Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()));
            _approvedPendingId=pending.Id;
            // Keep the one-time secret only until this exact device retrieves its credential.
        }
        finally {_mutation.Release();}Changed?.Invoke();
    }
    public async Task RevokeAsync()
    { await _mutation.WaitAsync();try {_identity.Pair(null);_pending=null;_invitation=null;}finally{_mutation.Release();}Changed?.Invoke(); }
    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                await _requests.WaitAsync(ct);TcpClient socket;
                try {socket=await _listener!.AcceptTcpClientAsync(ct);}catch{_requests.Release();throw;}
                long id=Interlocked.Increment(ref _clientId);var task=ServeAsync(socket,ct);_clients[id]=task;
                _=task.ContinueWith(completed=>{_clients.TryRemove(id,out _);},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {}
        catch(SocketException) when(ct.IsCancellationRequested) {}
        catch(Exception ex){Error=ex.Message;Changed?.Invoke();}
    }
    private async Task ServeAsync(TcpClient socket,CancellationToken stop)
    {
        using var client=socket;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));var ct=deadline.Token;
        try
        {
            using var tls=new SslStream(socket.GetStream(),false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {ServerCertificate=_identity.Certificate,
                EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},ct);
            var request=await CompanionWire.ReadAsync<CompanionRequest>(tls,ct);
            CompanionResponse response;
            await _mutation.WaitAsync(ct);try {response=await ProcessAsync(request,ct);}finally{_mutation.Release();}
            await CompanionWire.WriteAsync(tls,response,ct);
        }
        catch(AuthenticationException ex) {Error="TLS: "+ex.Message+" "+ex.InnerException?.Message;Changed?.Invoke();}
        catch(Exception ex) when(ex is IOException or OperationCanceledException or System.Text.Json.JsonException
            or ArgumentException or InvalidDataException) { /* Untrusted or disconnected peers cannot stop the listener. */ }
        catch(Exception ex) {Error="Ошибка синхронизации: "+ex.GetType().Name;Changed?.Invoke();}
        finally {_requests.Release();}
    }
    private static bool Equal(string left,string right)=>CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left),Encoding.UTF8.GetBytes(right));
    private CompanionResponse Reply(string status,string? token=null,long? ack=null,string? message=null)=>new(status,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),token,ack,message);
    private async Task<CompanionResponse> ProcessAsync(CompanionRequest r,CancellationToken ct)
    {
        if(r.Version!=1 || !Guid.TryParseExact(r.DeviceId,"D",out _) || r.DeviceName==null || r.Credential==null || r.DeviceName.Length is <1 or >64 || r.Credential.Length>128) return Reply("invalid");
        if(r.Action=="pair")
        {
            if(_invitation is not {} invitation || invitation.ExpiresUtcMs<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() || !Equal(invitation.Secret,r.Credential)) return Reply("unauthorized");
            if(_pending is not {} pending) {_pending=(r.DeviceId,r.DeviceName,r.Credential);Changed?.Invoke();return Reply("pending");}
            if(pending.Id!=r.DeviceId) return Reply("busy");
            if(Device is {} paired && paired.DeviceId==r.DeviceId && _approvedPendingId==r.DeviceId)
                return Reply("paired",paired.Token);
            return Reply("pending");
        }
        if(Device is not {} device || device.DeviceId!=r.DeviceId || !Equal(device.Token,r.Credential)) return Reply("unauthorized");
        if(_approvedPendingId==r.DeviceId) {_invitation=null;_pending=null;_approvedPendingId=null;Changed?.Invoke();}
        if(r.Action=="hello") return Reply("ok");
        if(r.Action!="sync" || r.Cycles==null) return Reply("invalid");
        long ack=await _importer.ImportAsync("android:"+device.DeviceId,r.Cycles,ct);
        LastSyncUtc=DateTime.UtcNow;Changed?.Invoke();return Reply("ok",ack:ack);
    }
    public async Task StopAsync()
    {
        if(_cts==null)return;await _cts.CancelAsync();_listener?.Stop();if(_loop!=null)await _loop;
        await Task.WhenAll(_clients.Values);_cts.Dispose();_cts=null;_listener=null;_invitation=null;_pending=null;Changed?.Invoke();
    }
    public async ValueTask DisposeAsync(){await StopAsync();_identity.Dispose();_mutation.Dispose();_requests.Dispose();}
}
