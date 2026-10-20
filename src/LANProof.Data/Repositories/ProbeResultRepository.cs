using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Data.Repositories;

/// <summary>
/// EF Core implementation of probe result persistence.
/// </summary>
public sealed class ProbeResultRepository : IProbeResultRepository
{
    private readonly LanProofDbContext _context;

    public ProbeResultRepository(LanProofDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(ProbeResult result, CancellationToken ct = default)
    {
        _context.ProbeResults.Add(result);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<ProbeResult> results, CancellationToken ct = default)
    {
        _context.ProbeResults.AddRange(results);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProbeResult>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        return await _context.ProbeResults
            .Where(r => r.Timestamp >= start && r.Timestamp <= end)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProbeResult>> GetByTypeAndTimeRangeAsync(ProbeType type, DateTime start, DateTime end, CancellationToken ct = default)
    {
        return await _context.ProbeResults
            .Where(r => r.ProbeType == type && r.Timestamp >= start && r.Timestamp <= end)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProbeResult>> GetLatestAsync(int count, CancellationToken ct = default)
    {
        return await _context.ProbeResults
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProbeResult>> GetLatestByTypeAsync(CancellationToken ct = default)
    {
        return await _context.ProbeResults
            .GroupBy(r => r.ProbeType)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default)
    {
        return await _context.ProbeResults
            .Where(r => r.Timestamp < threshold)
            .ExecuteDeleteAsync(ct);
    }
}
