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
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS accounts (
                jid TEXT PRIMARY KEY,
                password TEXT NOT NULL,
                resource TEXT NOT NULL,
                host TEXT,
                port INTEGER NOT NULL DEFAULT 5222,
                use_direct_tls INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                account_jid TEXT NOT NULL,
                remote_jid TEXT NOT NULL,
                sender_jid TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                direction INTEGER NOT NULL, -- 0 = Inbound, 1 = Outbound
                body TEXT NOT NULL,
                stanza_id TEXT,
                origin_id TEXT,
                replace_id TEXT,
                is_encrypted INTEGER NOT NULL DEFAULT 0,
                encryption_type TEXT,
                is_read INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_messages_chat ON messages(account_jid, remote_jid, timestamp);
            CREATE INDEX IF NOT EXISTS idx_messages_stanza_id ON messages(stanza_id);
            CREATE INDEX IF NOT EXISTS idx_messages_origin_id ON messages(origin_id);

            DELETE FROM messages
            WHERE rowid NOT IN (
                SELECT MIN(rowid)
                FROM messages
                GROUP BY account_jid, remote_jid, COALESCE(stanza_id, id), timestamp, body
            );

            CREATE TABLE IF NOT EXISTS message_reactions (
                account_jid TEXT NOT NULL,
                remote_jid TEXT NOT NULL,
                message_id TEXT NOT NULL,
                sender_jid TEXT NOT NULL,
                emoji TEXT NOT NULL,
                PRIMARY KEY (account_jid, remote_jid, message_id, sender_jid, emoji)
            );

            CREATE INDEX IF NOT EXISTS idx_reactions_msg ON message_reactions(account_jid, remote_jid, message_id);

            CREATE TABLE IF NOT EXISTS roster (
                account_jid TEXT NOT NULL,
                contact_jid TEXT NOT NULL,
                name TEXT,
                subscription TEXT NOT NULL DEFAULT 'none',
                groups TEXT,
                PRIMARY KEY (account_jid, contact_jid)
            );

            CREATE TABLE IF NOT EXISTS omemo_identities (
                account_jid TEXT PRIMARY KEY,
                device_id INTEGER NOT NULL,
                identity_key_private TEXT NOT NULL,
                identity_key_public TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS omemo_sessions (
                account_jid TEXT NOT NULL,
                remote_jid TEXT NOT NULL,
                device_id INTEGER NOT NULL,
                session_data BLOB NOT NULL,
                last_active TEXT NOT NULL,
                trust_state INTEGER NOT NULL DEFAULT 0, -- 0 = Undecided, 1 = Trusted, 2 = Untrusted
                PRIMARY KEY (account_jid, remote_jid, device_id)
            );

            CREATE TABLE IF NOT EXISTS omemo_prekeys (
                account_jid TEXT NOT NULL,
                key_id INTEGER NOT NULL,
                private_key TEXT NOT NULL,
                public_key TEXT NOT NULL,
                PRIMARY KEY (account_jid, key_id)
            );

            CREATE TABLE IF NOT EXISTS omemo_signed_prekeys (
                account_jid TEXT NOT NULL,
                key_id INTEGER NOT NULL,
                private_key TEXT NOT NULL,
                public_key TEXT NOT NULL,
                signature TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                PRIMARY KEY (account_jid, key_id)
            );
        """;
        cmd.ExecuteNonQuery();
    }

}
