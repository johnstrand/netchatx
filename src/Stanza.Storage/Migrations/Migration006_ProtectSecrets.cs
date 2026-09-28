using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using Stanza.Storage.Security;

namespace Stanza.Storage.Migrations;

public sealed class Migration006_ProtectSecrets(ISecretProtector secretProtector) : DatabaseMigration
{
    public override int Version => 6;
    public override string Description => "Encrypt account credentials and OMEMO private key material";

    public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, "PRAGMA secure_delete = ON;");

        ProtectSingleKeyTextColumn(connection, transaction, "accounts", "jid", "password");
        ProtectSingleKeyTextColumn(connection, transaction, "omemo_identities", "account_jid", "identity_key_private");
        ProtectCompositeKeyTextColumn(connection, transaction, "omemo_prekeys", "account_jid", "key_id", "private_key");
        ProtectCompositeKeyTextColumn(connection, transaction, "omemo_signed_prekeys", "account_jid", "key_id", "private_key");
        ProtectSessionData(connection, transaction);
    }

    private void ProtectSingleKeyTextColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string keyColumn,
        string valueColumn)
    {
        var rows = new List<(string Key, string Value)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {keyColumn}, {valueColumn} FROM {table};";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var row in rows)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {table} SET {valueColumn} = $value WHERE {keyColumn} = $key;";
            update.Parameters.AddWithValue("$value", SecretProtection.ProtectText(row.Value, secretProtector));
            update.Parameters.AddWithValue("$key", row.Key);
            update.ExecuteNonQuery();
        }
    }

    private void ProtectCompositeKeyTextColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string keyColumn1,
        string keyColumn2,
        string valueColumn)
    {
        var rows = new List<(string Key1, int Key2, string Value)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {keyColumn1}, {keyColumn2}, {valueColumn} FROM {table};";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
            }
        }

        foreach (var row in rows)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {table} SET {valueColumn} = $value WHERE {keyColumn1} = $key1 AND {keyColumn2} = $key2;";
            update.Parameters.AddWithValue("$value", SecretProtection.ProtectText(row.Value, secretProtector));
            update.Parameters.AddWithValue("$key1", row.Key1);
            update.Parameters.AddWithValue("$key2", row.Key2);
            update.ExecuteNonQuery();
        }
    }

    private void ProtectSessionData(SqliteConnection connection, SqliteTransaction transaction)
    {
        var rows = new List<(string AccountJid, string RemoteJid, int DeviceId, byte[] Value)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT account_jid, remote_jid, device_id, session_data FROM omemo_sessions;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (byte[])reader[3]));
            }
        }

        try
        {
            foreach (var row in rows)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE omemo_sessions SET session_data = $value WHERE account_jid = $account AND remote_jid = $remote AND device_id = $device;";
                update.Parameters.AddWithValue("$value", SecretProtection.ProtectBytes(row.Value, secretProtector));
                update.Parameters.AddWithValue("$account", row.AccountJid);
                update.Parameters.AddWithValue("$remote", row.RemoteJid);
                update.Parameters.AddWithValue("$device", row.DeviceId);
                update.ExecuteNonQuery();
            }
        }
        finally
        {
            foreach (var row in rows)
            {
                CryptographicOperations.ZeroMemory(row.Value);
            }
        }
    }
}
