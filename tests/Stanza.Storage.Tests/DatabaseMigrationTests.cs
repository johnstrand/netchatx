using Microsoft.Data.Sqlite;
using Stanza.Storage.Migrations;

namespace Stanza.Storage.Tests;

public class DatabaseMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _context;

    public DatabaseMigrationTests()
    {
        _dbPath = $"test_migration_{Guid.NewGuid():N}.db";
        _context = new DatabaseContext(_dbPath);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void DatabaseContext_InitializesWithAllMigrationsApplied()
    {
        var currentVersion = _context.GetCurrentSchemaVersion();
        var appliedVersions = _context.GetAppliedMigrationVersions();

        Assert.Equal(4, currentVersion);
        Assert.Equal([1, 2, 3, 4], appliedVersions);
    }

    [Fact]
    public void DatabaseMigrator_IsIdempotent()
    {
        using var connection = _context.CreateConnection();
        var migrator = new DatabaseMigrator();

        // Run migrate again - should not throw and versions should remain identical
        migrator.Migrate(connection);

        var appliedVersions = DatabaseMigrator.GetAppliedVersions(connection);
        Assert.Equal(4, appliedVersions.Count);
        Assert.Contains(1, appliedVersions);
        Assert.Contains(2, appliedVersions);
        Assert.Contains(3, appliedVersions);
        Assert.Contains(4, appliedVersions);
    }

    [Fact]
    public void DatabaseMigrator_UpgradesLegacyDatabaseWithoutVersionTable()
    {
        var legacyDbPath = $"legacy_{Guid.NewGuid():N}.db";
        try
        {
            // Simulate legacy database without schema_versions table and without raw_xml column
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = legacyDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE messages (
                        id TEXT PRIMARY KEY,
                        account_jid TEXT NOT NULL,
                        remote_jid TEXT NOT NULL,
                        sender_jid TEXT NOT NULL,
                        timestamp TEXT NOT NULL,
                        direction INTEGER NOT NULL,
                        body TEXT NOT NULL,
                        stanza_id TEXT,
                        origin_id TEXT,
                        replace_id TEXT,
                        is_encrypted INTEGER NOT NULL DEFAULT 0,
                        encryption_type TEXT,
                        is_read INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE accounts (
                        jid TEXT PRIMARY KEY,
                        password TEXT NOT NULL,
                        resource TEXT NOT NULL,
                        host TEXT,
                        port INTEGER NOT NULL DEFAULT 5222,
                        use_direct_tls INTEGER NOT NULL DEFAULT 0,
                        is_active INTEGER NOT NULL DEFAULT 1
                    );
                    """;
                cmd.ExecuteNonQuery();
            }

            // Now initialize via DatabaseContext
            using var legacyContext = new DatabaseContext(legacyDbPath);
            Assert.Equal(4, legacyContext.GetCurrentSchemaVersion());

            // Verify raw_xml column was added
            using var verifyConn = legacyContext.CreateConnection();
            using var checkColCmd = verifyConn.CreateCommand();
            checkColCmd.CommandText = "PRAGMA table_info(messages);";
            using var reader = checkColCmd.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
            Assert.Contains("raw_xml", columns);
        }
        finally
        {
            if (File.Exists(legacyDbPath))
            {
                try { File.Delete(legacyDbPath); } catch { }
            }
        }
    }

    [Fact]
    public void DatabaseMigrator_RollsBackOnFailure()
    {
        var testDbPath = $"fail_test_{Guid.NewGuid():N}.db";
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = testDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            var failingMigration = new FailingTestMigration();
            var migrator = new DatabaseMigrator([failingMigration]);

            Assert.Throws<InvalidOperationException>(() => migrator.Migrate(conn));

            var versions = DatabaseMigrator.GetAppliedVersions(conn);
            Assert.Empty(versions);
        }
        finally
        {
            if (File.Exists(testDbPath))
            {
                try { File.Delete(testDbPath); } catch { }
            }
        }
    }

    private sealed class FailingTestMigration : DatabaseMigration
    {
        public override int Version => 99;
        public override string Description => "Failing migration test";

        public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteNonQuery(connection, transaction, "CREATE TABLE temp_test (id INTEGER PRIMARY KEY);");
            throw new InvalidOperationException("Simulated migration failure");
        }
    }
}
