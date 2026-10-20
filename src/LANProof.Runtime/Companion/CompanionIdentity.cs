using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LANProof.Runtime.Companion;

public sealed record PairedCompanion(string DeviceId,string Name,string Token);

internal sealed class CompanionIdentity : IDisposable
{
    private readonly string _path;
    public X509Certificate2 Certificate { get; }
    public PairedCompanion? Device { get; private set; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
    private sealed record State(string Certificate,PairedCompanion? Device);
    public CompanionIdentity(string directory)
    {
        Directory.CreateDirectory(directory);_path=Path.Combine(directory,"companion.identity");
        if(File.Exists(_path))
        {
            var bytes=Unprotect(File.ReadAllBytes(_path));var state=JsonSerializer.Deserialize<State>(bytes)!;
            Certificate=new X509Certificate2(Convert.FromBase64String(state.Certificate),(string?)null,KeyFlags);
            Device=state.Device;
        }
        else
        {
            using var key=RSA.Create(2048);var request=new CertificateRequest("CN=LANProof Companion",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            using var temporary=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(3));
            Certificate=new X509Certificate2(temporary.Export(X509ContentType.Pfx),(string?)null,KeyFlags);Save();
        }
    }
    // Windows Schannel requires a user key container; the OS removes this temporary key on Dispose.
    private static X509KeyStorageFlags KeyFlags=>X509KeyStorageFlags.Exportable |
        (OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    public void Pair(PairedCompanion? device) { Device=device;Save(); }
    private void Save()
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new State(Convert.ToBase64String(Certificate.Export(X509ContentType.Pfx)),Device));
        File.WriteAllBytes(_path+".tmp",Protect(bytes));
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(_path+".tmp",UnixFileMode.UserRead|UnixFileMode.UserWrite);
        File.Move(_path+".tmp",_path,true);
    }
    private static byte[] Protect(byte[] value)=>OperatingSystem.IsWindows() ? ProtectedData.Protect(value,null,DataProtectionScope.CurrentUser) : value;
    private static byte[] Unprotect(byte[] value)=>OperatingSystem.IsWindows() ? ProtectedData.Unprotect(value,null,DataProtectionScope.CurrentUser) : value;
    public void Dispose()=>Certificate.Dispose();
}
