using Microsoft.Data.Sqlite;
using System.Text;
using Stanza.Storage.Migrations;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Stanza.Storage.Security;

namespace Stanza.Storage.Tests;

public sealed class ProtectedSecretsTests
{
    [Fact]
    public async Task Repositories_EncryptAccountAndOmemoSecretsAtRest()
    {
        var path = CreateDatabasePath();
        try
        {
            using var context = new DatabaseContext(path, TestSecretProtector.Instance);
            var accounts = new AccountRepository(context);
            var omemo = new OmemoRepository(context);
            var profile = new AccountProfile
            {
                Jid = "alice@example.com",
                Password = "account-password",
                IsActive = true
            };
            await accounts.SaveAccountAsync(profile);
            await omemo.SaveIdentityAsync(profile.Jid, 1234, "identity-private", "identity-public");
            await omemo.SaveSessionAsync(new OmemoSessionRecord
            {
                AccountJid = profile.Jid,
                RemoteJid = "bob@example.com",
                DeviceId = 5678,
                SessionData = [1, 2, 3, 4],
                LastActive = DateTimeOffset.UtcNow,
                TrustState = OmemoTrustState.Trusted
            });

            using (var connection = context.CreateConnection())
            {
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT password FROM accounts WHERE jid = 'alice@example.com';"));
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT identity_key_private FROM omemo_identities WHERE account_jid = 'alice@example.com';"));
                Assert.NotEqual(new byte[] { 1, 2, 3, 4 }, ReadBytes(connection, "SELECT session_data FROM omemo_sessions WHERE account_jid = 'alice@example.com';"));
            }

            Assert.Equal("account-password", (await accounts.GetAccountAsync(profile.Jid))?.Password);
            Assert.Equal("identity-private", (await omemo.GetIdentityAsync(profile.Jid))?.PrivateKey);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, (await omemo.GetSessionAsync(profile.Jid, "bob@example.com", 5678))?.SessionData);
            context.Dispose();

            var databaseText = ReadDatabaseText(path);
            Assert.DoesNotContain("account-password", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("identity-private", databaseText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task Migration_EncryptsExistingAccountAndOmemoSecrets()
    {
        var path = CreateDatabasePath();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                new DatabaseMigrator(DatabaseMigrator.DefaultMigrations.Take(5), TestSecretProtector.Instance).Migrate(connection);

                using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO accounts (jid, password, resource, port, is_active)
                    VALUES ('legacy@example.com', 'legacy-password', 'Stanza', 5222, 1);
                    INSERT INTO omemo_identities (account_jid, device_id, identity_key_private, identity_key_public)
                    VALUES ('legacy@example.com', 111, 'legacy-identity-private', 'legacy-identity-public');
                    INSERT INTO omemo_prekeys (account_jid, key_id, private_key, public_key)
                    VALUES ('legacy@example.com', 222, 'legacy-prekey-private', 'legacy-prekey-public');
                    INSERT INTO omemo_signed_prekeys (account_jid, key_id, private_key, public_key, signature, timestamp)
                    VALUES ('legacy@example.com', 333, 'legacy-signed-private', 'legacy-signed-public', 'signature', '2026-01-01T00:00:00Z');
                    INSERT INTO omemo_sessions (account_jid, remote_jid, device_id, session_data, last_active)
                    VALUES ('legacy@example.com', 'peer@example.com', 444, X'01020304', '2026-01-01T00:00:00Z');
                    """;
                insert.ExecuteNonQuery();
                SqliteConnection.ClearPool(connection);
            }

            using var context = new DatabaseContext(path, TestSecretProtector.Instance);
            Assert.Equal(6, context.GetCurrentSchemaVersion());

            using (var connection = context.CreateConnection())
            {
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT password FROM accounts WHERE jid = 'legacy@example.com';"));
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT identity_key_private FROM omemo_identities WHERE account_jid = 'legacy@example.com';"));
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT private_key FROM omemo_prekeys WHERE key_id = 222;"));
                Assert.StartsWith("stz:enc:v1:", ReadString(connection, "SELECT private_key FROM omemo_signed_prekeys WHERE key_id = 333;"));
                Assert.NotEqual(new byte[] { 1, 2, 3, 4 }, ReadBytes(connection, "SELECT session_data FROM omemo_sessions WHERE device_id = 444;"));
            }

            var accounts = new AccountRepository(context);
            var omemo = new OmemoRepository(context);
            Assert.Equal("legacy-password", (await accounts.GetAccountAsync("legacy@example.com"))?.Password);
            Assert.Equal("legacy-identity-private", (await omemo.GetIdentityAsync("legacy@example.com"))?.PrivateKey);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, (await omemo.GetSessionAsync("legacy@example.com", "peer@example.com", 444))?.SessionData);
            context.Dispose();

            var databaseText = ReadDatabaseText(path);
            Assert.DoesNotContain("legacy-password", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-identity-private", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-prekey-private", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-signed-private", databaseText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), $"stanza-secrets-{Guid.NewGuid():N}.db");

    private static string ReadString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    private static byte[] ReadBytes(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (byte[])command.ExecuteScalar()!;
    }

    private static string ReadDatabaseText(string path)
    {
        var contents = new StringBuilder(Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecarPath = path + suffix;
            if (File.Exists(sidecarPath))
            {
                contents.Append(Encoding.UTF8.GetString(File.ReadAllBytes(sidecarPath)));
            }
        }

        return contents.ToString();
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // SQLite may keep a pooled handle open briefly after a connection closes.
                }
            }
        }
    }
}
