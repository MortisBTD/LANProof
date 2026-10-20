namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Repository for persisting and querying network events.
/// </summary>
public interface INetworkEventRepository
{
    /// <summary>Save a network event.</summary>
    Task AddAsync(NetworkEvent networkEvent, CancellationToken ct = default);

    /// <summary>Update an existing event (e.g., mark as resolved).</summary>
    Task UpdateAsync(NetworkEvent networkEvent, CancellationToken ct = default);

    /// <summary>Get events within a time range.</summary>
    Task<IReadOnlyList<NetworkEvent>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken ct = default);

    /// <summary>Get all currently ongoing events.</summary>
    Task<IReadOnlyList<NetworkEvent>> GetOngoingAsync(CancellationToken ct = default);

    /// <summary>Get the latest N events.</summary>
    Task<IReadOnlyList<NetworkEvent>> GetLatestAsync(int count, CancellationToken ct = default);

    /// <summary>Delete events older than the specified date.</summary>
    Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default);
}
