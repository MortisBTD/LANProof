using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Data.Repositories;

/// <summary>
/// EF Core implementation of network event persistence.
/// </summary>
public sealed class NetworkEventRepository : INetworkEventRepository
{
    private readonly LanProofDbContext _context;

    public NetworkEventRepository(LanProofDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(NetworkEvent networkEvent, CancellationToken ct = default)
    {
        _context.NetworkEvents.Add(networkEvent);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(NetworkEvent networkEvent, CancellationToken ct = default)
    {
        _context.NetworkEvents.Update(networkEvent);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkEvent>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        return await _context.NetworkEvents
            .Where(e => e.StartTime <= end && (e.EndTime == null || e.EndTime >= start))
            .OrderByDescending(e => e.StartTime)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkEvent>> GetOngoingAsync(CancellationToken ct = default)
    {
        return await _context.NetworkEvents
            .Where(e => e.EndTime == null)
            .OrderByDescending(e => e.StartTime)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkEvent>> GetLatestAsync(int count, CancellationToken ct = default)
    {
        return await _context.NetworkEvents
            .OrderByDescending(e => e.StartTime)
            .Take(count)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default)
    {
        return await _context.NetworkEvents
            .Where(e => e.EndTime != null && e.EndTime < threshold)
            .ExecuteDeleteAsync(ct);
    }
}
