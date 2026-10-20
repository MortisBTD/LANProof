using System.Diagnostics;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.Extensions.Logging;

namespace LANProof.Probes.Implementations;

/// <summary>
/// Checks HTTP/HTTPS endpoint availability.
/// </summary>
public sealed class HttpProbe : IProbe, IDisposable
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5), PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 4, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(3)
    };
    private readonly ILogger<HttpProbe> _logger;
    private readonly ProbeConfiguration _config;
    private readonly HttpClient _httpClient;

    /// <inheritdoc />
    public ProbeType Type => ProbeType.Http;

    /// <inheritdoc />
    public string Name => "HTTP Check";

    public HttpProbe(ILogger<HttpProbe> logger, ProbeConfiguration config, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        _config = config;
        _httpClient = handler == null ? new HttpClient(SharedHandler, disposeHandler: false) : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LANProof/0.1");
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = new ProbeResult
        {
            ProbeType = ProbeType.Http,
            ConnectionType = NetworkHelper.GetConnectionType(),
            AdapterName = NetworkHelper.GetActiveAdapterName()
        };

        var responses = await Task.WhenAll(_config.HttpTargets.Select(async url =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sw = Stopwatch.StartNew();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(_config.TimeoutMs);
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                string method = "HEAD";
                if (response.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented)
                {
                    response.Dispose();
                    using var fallback = new HttpRequestMessage(HttpMethod.Get, url);
                    fallback.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    response = await _httpClient.SendAsync(fallback, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    method = "HEAD + GET fallback";
                }
                using var completedResponse = response;
                sw.Stop();

                return (Success: response.IsSuccessStatusCode, Ms: (double?)sw.Elapsed.TotalMilliseconds,
                    Status: (int?)response.StatusCode, Details: $"{url}: {(int)response.StatusCode}, {method}, response headers={sw.Elapsed.TotalMilliseconds:F0}ms");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return (Success: false, Ms: (double?)null, Status: (int?)null, Details: $"{url}: FAILED ({ex.Message})");
            }
        }));
        var responseTimes = responses.Where(r => r.Success && r.Ms.HasValue).Select(r => r.Ms!.Value).ToList();
        int failures = responses.Count(r => !r.Success);
        result.Target = string.Join(", ", _config.HttpTargets);

        if (responseTimes.Count > 0)
        {
            result.IsSuccess = true;
            result.RoundTripMs = responseTimes.Average();
            result.HttpStatusCode = responses.LastOrDefault(r => r.Status.HasValue).Status;
            result.PacketLossPercent = (double)failures / _config.HttpTargets.Count * 100;
        }
        else
        {
            result.IsSuccess = false;
            result.PacketLossPercent = 100;
        }

        result.Details = "HTTPS response headers (includes service processing; DNS/TCP/TLS on a cold connection; not ICMP RTT). " + string.Join(" | ", responses.Select(r => r.Details));
        return result;
    }

    public void Dispose() => _httpClient.Dispose();
}
