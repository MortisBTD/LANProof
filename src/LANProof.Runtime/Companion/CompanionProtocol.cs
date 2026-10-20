using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using LANProof.Core.Models;

namespace LANProof.Runtime.Companion;

public sealed record CompanionObservation(string ProbeType, string Target, bool IsSuccess, bool IsConclusive,
    double? RoundTripMs=null, double? JitterMs=null, double? PacketLossPercent=null,
    double? DnsResolveMs=null, int? HttpStatusCode=null, string? Details=null);
public sealed record CompanionCycle(long Sequence, long TimestampUtcMs, double? ClockUncertaintyMs, List<CompanionObservation> Observations);
public sealed record CompanionRequest(int Version=1, string Action="hello", string DeviceId="", string DeviceName="",
    string Credential="", List<CompanionCycle>? Cycles=null);
public sealed record CompanionResponse(string Status, long ServerUtcMs, string? Token=null, long? AcknowledgedThrough=null, string? Message=null);
public sealed record PairingInvitation(int Version, string Host, int Port, string Fingerprint, string Secret, long ExpiresUtcMs)
{
    public string ToCode() => "lanproof://pair?"+string.Join("&",new Dictionary<string,string>
    { ["v"]="1",["host"]=Host,["port"]=Port.ToString(),["pin"]=Fingerprint,["secret"]=Secret,["expires"]=ExpiresUtcMs.ToString() }
        .Select(k=>$"{k.Key}={Uri.EscapeDataString(k.Value)}"));
}

/// <summary>Protocol v1: TLS 1.2+; a big-endian 32-bit byte length followed by UTF-8 JSON. One exchange per connection.</summary>
public static class CompanionWire
{
    public const int MaxFrameBytes=256*1024;
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web)
    { Converters={new JsonStringEnumConverter()} };
    public static async Task WriteAsync<T>(Stream stream,T value,CancellationToken ct=default)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
        if(bytes.Length>MaxFrameBytes) throw new InvalidDataException("Frame too large");
        var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,bytes.Length);
        await stream.WriteAsync(length,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream,CancellationToken ct=default)
    {
        var length=new byte[4];await stream.ReadExactlyAsync(length,ct);
        int count=BinaryPrimitives.ReadInt32BigEndian(length);
        if(count is <2 or >MaxFrameBytes) throw new InvalidDataException("Invalid frame size");
        var bytes=new byte[count];await stream.ReadExactlyAsync(bytes,ct);
        return JsonSerializer.Deserialize<T>(bytes,Json) ?? throw new InvalidDataException("Missing payload");
    }
}
