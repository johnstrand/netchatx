using Microsoft.Data.Sqlite;
using NetChatx.Storage.Models;

namespace NetChatx.Storage.Repositories;

public sealed class MessageRepository
{
    private readonly DatabaseContext _context;

    public MessageRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO messages (
                id, account_jid, remote_jid, sender_jid, timestamp, direction,
                body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read
            ) VALUES (
                $id, $account_jid, $remote_jid, $sender_jid, $timestamp, $direction,
                $body, $stanza_id, $origin_id, $replace_id, $is_encrypted, $encryption_type, $is_read
            )
            ON CONFLICT(id) DO UPDATE SET
                body = excluded.body,
                replace_id = excluded.replace_id,
                is_read = excluded.is_read;
        """;

        cmd.Parameters.AddWithValue("$id", message.Id);
        cmd.Parameters.AddWithValue("$account_jid", message.AccountJid);
        cmd.Parameters.AddWithValue("$remote_jid", message.RemoteJid);
        cmd.Parameters.AddWithValue("$sender_jid", message.SenderJid);
        cmd.Parameters.AddWithValue("$timestamp", message.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$direction", (int)message.Direction);
        cmd.Parameters.AddWithValue("$body", message.Body);
        cmd.Parameters.AddWithValue("$stanza_id", (object?)message.StanzaId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$origin_id", (object?)message.OriginId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$replace_id", (object?)message.ReplaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$is_encrypted", message.IsEncrypted ? 1 : 0);
        cmd.Parameters.AddWithValue("$encryption_type", (object?)message.EncryptionType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$is_read", message.IsRead ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(
        string accountJid,
        string remoteJid,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        if (before.HasValue)
        {
            cmd.CommandText = """
                SELECT id, account_jid, remote_jid, sender_jid, timestamp, direction,
                       body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read
                FROM messages
                WHERE account_jid = $account_jid AND remote_jid = $remote_jid AND timestamp < $before
                ORDER BY timestamp DESC
                LIMIT $limit;
            """;
            cmd.Parameters.AddWithValue("$before", before.Value.ToString("O"));
        }
        else
        {
            cmd.CommandText = """
                SELECT id, account_jid, remote_jid, sender_jid, timestamp, direction,
                       body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read
                FROM messages
                WHERE account_jid = $account_jid AND remote_jid = $remote_jid
                ORDER BY timestamp DESC
                LIMIT $limit;
            """;
        }

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$remote_jid", remoteJid);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<ChatMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadMessage(reader));
        }

        // Return chronological order (oldest first)
        list.Reverse();
        return list;
    }

    public async Task<List<ChatMessage>> SearchMessagesAsync(
        string accountJid,
        string searchQuery,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT id, account_jid, remote_jid, sender_jid, timestamp, direction,
                   body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read
            FROM messages
            WHERE account_jid = $account_jid AND body LIKE $query
            ORDER BY timestamp DESC
            LIMIT $limit;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$query", $"%{searchQuery}%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<ChatMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadMessage(reader));
        }

        return list;
    }

    public async Task<bool> UpdateMessageByReplaceIdAsync(
        string accountJid,
        string replaceId,
        string newBody,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            UPDATE messages
            SET body = $body, replace_id = $replace_id
            WHERE account_jid = $account_jid AND (id = $replace_id OR stanza_id = $replace_id OR origin_id = $replace_id);
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$replace_id", replaceId);
        cmd.Parameters.AddWithValue("$body", newBody);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static ChatMessage ReadMessage(SqliteDataReader reader)
    {
        return new ChatMessage
        {
            Id = reader.GetString(0),
            AccountJid = reader.GetString(1),
            RemoteJid = reader.GetString(2),
            SenderJid = reader.GetString(3),
            Timestamp = DateTimeOffset.Parse(reader.GetString(4)),
            Direction = (MessageDirection)reader.GetInt32(5),
            Body = reader.GetString(6),
            StanzaId = reader.IsDBNull(7) ? null : reader.GetString(7),
            OriginId = reader.IsDBNull(8) ? null : reader.GetString(8),
            ReplaceId = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsEncrypted = reader.GetInt32(10) == 1,
            EncryptionType = reader.IsDBNull(11) ? null : reader.GetString(11),
            IsRead = reader.GetInt32(12) == 1
        };
    }
}
