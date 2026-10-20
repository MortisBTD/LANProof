namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Repository for persisting and querying probe results.
/// </summary>
public interface IProbeResultRepository
{
    /// <summary>Save a probe result.</summary>
    Task AddAsync(ProbeResult result, CancellationToken ct = default);

    /// <summary>Save multiple probe results.</summary>
    Task AddRangeAsync(IEnumerable<ProbeResult> results, CancellationToken ct = default);

    /// <summary>Get probe results within a time range.</summary>
    Task<IReadOnlyList<ProbeResult>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken ct = default);

    /// <summary>Get probe results by type within a time range.</summary>
    Task<IReadOnlyList<ProbeResult>> GetByTypeAndTimeRangeAsync(ProbeType type, DateTime start, DateTime end, CancellationToken ct = default);

    /// <summary>Get the latest N results.</summary>
    Task<IReadOnlyList<ProbeResult>> GetLatestAsync(int count, CancellationToken ct = default);

    /// <summary>Get the latest result for each probe type.</summary>
    Task<IReadOnlyList<ProbeResult>> GetLatestByTypeAsync(CancellationToken ct = default);

    /// <summary>Delete results older than the specified date.</summary>
    Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default);
}
