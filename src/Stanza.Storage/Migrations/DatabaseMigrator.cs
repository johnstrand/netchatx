using Microsoft.Data.Sqlite;
using Stanza.Storage.Security;

namespace Stanza.Storage.Migrations;

public sealed class DatabaseMigrator
{
    public static IReadOnlyList<IDatabaseMigration> DefaultMigrations { get; } = CreateDefaultMigrations(new OsSecretProtector());

    private readonly IReadOnlyList<IDatabaseMigration> _migrations;

    public DatabaseMigrator(IEnumerable<IDatabaseMigration>? migrations = null, ISecretProtector? secretProtector = null)
    {
        _migrations = (migrations ?? CreateDefaultMigrations(secretProtector ?? new OsSecretProtector()))
            .OrderBy(m => m.Version)
            .ToList();
    }

    public void Migrate(SqliteConnection connection)
    {
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            pragmaCmd.ExecuteNonQuery();
        }

        using (var initCmd = connection.CreateCommand())
        {
            initCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_versions (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL,
                    description TEXT NOT NULL
                );
                """;
            initCmd.ExecuteNonQuery();
        }

        var appliedVersions = GetAppliedVersions(connection);

        var protectedSecretsMigrated = false;
        foreach (var migration in _migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                migration.Apply(connection, transaction);
                protectedSecretsMigrated |= migration is Migration006_ProtectSecrets;

                using var recordCmd = connection.CreateCommand();
                recordCmd.Transaction = transaction;
                recordCmd.CommandText = """
                    INSERT INTO schema_versions (version, applied_at, description)
                    VALUES (@version, @applied_at, @description);
                    """;
                recordCmd.Parameters.AddWithValue("@version", migration.Version);
                recordCmd.Parameters.AddWithValue("@applied_at", DateTimeOffset.UtcNow.ToString("O"));
                recordCmd.Parameters.AddWithValue("@description", migration.Description);
                recordCmd.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        if (protectedSecretsMigrated)
        {
            using var checkpointCmd = connection.CreateCommand();
            checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpointCmd.ExecuteNonQuery();

            using var vacuumCmd = connection.CreateCommand();
            vacuumCmd.CommandText = "VACUUM;";
            vacuumCmd.ExecuteNonQuery();

            using var finalCheckpointCmd = connection.CreateCommand();
            finalCheckpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            finalCheckpointCmd.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<IDatabaseMigration> CreateDefaultMigrations(ISecretProtector secretProtector) =>
    [
        new Migration001_InitialSchema(),
        new Migration002_AddRawXmlColumn(),
        new Migration003_AddAllowUntrustedCertificatesColumn(),
        new Migration004_DeduplicateMessages(),
        new Migration005_AddAccountLabelAndColorHexColumns(),
        new Migration006_ProtectSecrets(secretProtector)
    ];

    public static HashSet<int> GetAppliedVersions(SqliteConnection connection)
    {
        var applied = new HashSet<int>();
        using var checkTableCmd = connection.CreateCommand();
        checkTableCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_versions';";
        var count = Convert.ToInt64(checkTableCmd.ExecuteScalar());
        if (count == 0)
        {
            return applied;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_versions ORDER BY version ASC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            applied.Add(reader.GetInt32(0));
        }

        return applied;
    }
}
