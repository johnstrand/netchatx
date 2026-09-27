using Microsoft.Data.Sqlite;
using Stanza.Storage.Migrations;

namespace Stanza.Storage;

public sealed class DatabaseContext : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    public static string GetDefaultDatabasePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stanza");
        Directory.CreateDirectory(dir);
        var targetDb = Path.Combine(dir, "stanza.db");

        // Migrate legacy database from working directory or app directory if present and target does not exist yet
        if (!File.Exists(targetDb))
        {
            string[] legacyPaths = [
                "stanza.db",
                Path.Combine(AppContext.BaseDirectory, "stanza.db")
            ];
            foreach (var legacy in legacyPaths)
            {
                if (File.Exists(legacy) && !string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(targetDb), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Copy(legacy, targetDb, overwrite: false);
                        break;
                    }
                    catch { }
                }
            }
        }

        return targetDb;
    }

    public DatabaseContext(string? databasePath = null)
    {
        databasePath ??= GetDefaultDatabasePath();

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    public SqliteConnection CreateConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        var migrator = new DatabaseMigrator();
        migrator.Migrate(connection);
    }

    public int GetCurrentSchemaVersion()
    {
        using var connection = CreateConnection();
        var versions = DatabaseMigrator.GetAppliedVersions(connection);
        return versions.Count > 0 ? versions.Max() : 0;
    }

    public IReadOnlyList<int> GetAppliedMigrationVersions()
    {
        using var connection = CreateConnection();
        return DatabaseMigrator.GetAppliedVersions(connection).OrderBy(v => v).ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}
