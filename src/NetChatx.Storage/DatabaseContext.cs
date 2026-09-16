using Microsoft.Data.Sqlite;

namespace NetChatx.Storage;

public sealed class DatabaseContext
{
    private readonly string _connectionString;

    public static string GetDefaultDatabasePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetChatx");
        Directory.CreateDirectory(dir);
        var targetDb = Path.Combine(dir, "netchatx.db");

        // Migrate legacy database from working directory or app directory if present and target does not exist yet
        if (!File.Exists(targetDb))
        {
            string[] legacyPaths = [
                "netchatx.db",
                Path.Combine(AppContext.BaseDirectory, "netchatx.db")
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
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        ExecuteSchemaScript(connection);
        EnsureRawXmlColumnExists(connection);
    }

    private static void ExecuteSchemaScript(SqliteConnection connection)
    {
        var assembly = typeof(DatabaseContext).Assembly;
        const string resourceName = "NetChatx.Storage.Resources.schema.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        string schemaSql = reader.ReadToEnd();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = schemaSql;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureRawXmlColumnExists(SqliteConnection connection)
    {
        using var checkColCmd = connection.CreateCommand();
        checkColCmd.CommandText = "PRAGMA table_info(messages);";
        using var reader = checkColCmd.ExecuteReader();
        bool hasRawXml = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "raw_xml", StringComparison.OrdinalIgnoreCase))
            {
                hasRawXml = true;
                break;
            }
        }
        if (!hasRawXml)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE messages ADD COLUMN raw_xml TEXT;";
            alterCmd.ExecuteNonQuery();
        }
    }

}
