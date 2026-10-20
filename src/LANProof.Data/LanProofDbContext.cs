using LANProof.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LANProof.Data;

/// <summary>
/// Entity Framework Core database context for LANProof.
/// </summary>
public sealed class LanProofDbContext : DbContext
{
    /// <summary>Probe results table.</summary>
    public DbSet<ProbeResult> ProbeResults => Set<ProbeResult>();

    /// <summary>Network events table.</summary>
    public DbSet<NetworkEvent> NetworkEvents => Set<NetworkEvent>();

    public LanProofDbContext(DbContextOptions<LanProofDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ProbeType);
            entity.HasIndex(e => new { e.ProbeType, e.Timestamp });
            entity.Property(e => e.ProbeType).HasConversion<string>();
            entity.Property(e => e.ConnectionType).HasConversion<string>();
        });

        modelBuilder.Entity<NetworkEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StartTime).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.EndTime).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.EndTime);
            entity.HasIndex(e => e.Severity);
            entity.Property(e => e.Severity).HasConversion<string>();
            entity.Property(e => e.Category).HasConversion<string>();
            entity.Property(e => e.Layer).HasConversion<string>();
            entity.Property(e => e.ConnectionType).HasConversion<string>();
            entity.Ignore(e => e.IsOngoing);
            entity.Ignore(e => e.Duration);
        });

    }
}
