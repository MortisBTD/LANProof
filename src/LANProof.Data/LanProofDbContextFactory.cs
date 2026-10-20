using Microsoft.EntityFrameworkCore;

namespace LANProof.Data;

/// <summary>
/// Factory for creating and configuring the LANProof database context.
/// </summary>
public static class LanProofDbContextFactory
{
    private static readonly object SchemaGate = new();
    private static readonly HashSet<string> Initialized = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Default database file name.</summary>
    public const string DefaultDbName = "lanproof.db";

    /// <summary>
    /// Create DbContextOptions for the given database path.
    /// </summary>
    public static DbContextOptions<LanProofDbContext> CreateOptions(string? dbPath = null)
    {
        dbPath ??= GetDefaultDbPath();
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new DbContextOptionsBuilder<LanProofDbContext>();
        builder.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        return builder.Options;
    }

    /// <summary>
    /// Create a new database context with default settings.
    /// </summary>
    public static LanProofDbContext Create(string? dbPath = null)
    {
        var context = new LanProofDbContext(CreateOptions(dbPath));
        var key = context.Database.GetConnectionString()!;
        lock (SchemaGate)
        {
            if (!Initialized.Contains(key))
            {
                context.Database.EnsureCreated();
                context.Database.OpenConnection();
                try
                {
                    using var command = context.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "PRAGMA table_info(ProbeResults)";
                    bool hasConclusive = false;
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) hasConclusive |= reader.GetString(1) == "IsConclusive";
                    if (!hasConclusive) context.Database.ExecuteSqlRaw("ALTER TABLE ProbeResults ADD COLUMN IsConclusive INTEGER NOT NULL DEFAULT 1");
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ProbeResults') WHERE name='ClockUncertaintyMs'";
                    if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                        context.Database.ExecuteSqlRaw("ALTER TABLE ProbeResults ADD COLUMN ClockUncertaintyMs REAL NULL DEFAULT 0");
                    context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS CompanionReceipts (DeviceId TEXT NOT NULL, Sequence INTEGER NOT NULL, TimestampUtcMs INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(DeviceId, Sequence))");
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CompanionReceipts') WHERE name='TimestampUtcMs'";
                    if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                        context.Database.ExecuteSqlRaw("ALTER TABLE CompanionReceipts ADD COLUMN TimestampUtcMs INTEGER NOT NULL DEFAULT 0");
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DiagnosticReports'";
                    if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM DiagnosticReports";
                        if (Convert.ToInt32(command.ExecuteScalar()) == 0) context.Database.ExecuteSqlRaw("DROP TABLE DiagnosticReports");
                    }
                    Initialized.Add(key);
                }
                finally { context.Database.CloseConnection(); }
            }
        }
        return context;
    }

    /// <summary>
    /// Get the default database file path.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LANProof", DefaultDbName);
    }
}
