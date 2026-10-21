using System.Security.Cryptography;
using System.Text;
using LANProof.Runtime;

namespace LANProof.App;

/// <summary>One Windows process per data directory. A second launch only requests the window.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _restore;
    private RegisteredWaitHandle? _registration;
    public bool IsPrimary { get; }

    public SingleInstance(string? scope = null)
    {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            (scope ?? ApplicationPaths.DataDirectory).ToUpperInvariant())))[..24];
        var name=@"Local\LANProof-"+hash;
        _mutex=new Mutex(true,name,out var created);
        IsPrimary=created;
        _restore=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-restore");
        if (!IsPrimary) _restore.Set();
    }

    public void Listen(Action restore)
    {
        if (!IsPrimary) return;
        _registration=ThreadPool.RegisterWaitForSingleObject(_restore,(_,_)=>restore(),null,
            Timeout.Infinite,false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _restore.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
