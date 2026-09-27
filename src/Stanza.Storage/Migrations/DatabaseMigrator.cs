using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public sealed class DatabaseMigrator
{
    public static readonly IReadOnlyList<IDatabaseMigration> DefaultMigrations =
    [
        new Migration001_InitialSchema(),
        new Migration002_AddRawXmlColumn(),
        new Migration003_AddAllowUntrustedCertificatesColumn(),
        new Migration004_DeduplicateMessages()
    ];

    private readonly IReadOnlyList<IDatabaseMigration> _migrations;

    public DatabaseMigrator(IEnumerable<IDatabaseMigration>? migrations = null)
    {
        _migrations = (migrations ?? DefaultMigrations)
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
    }

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
