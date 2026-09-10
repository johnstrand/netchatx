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

    public Task SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        return SaveMessagesAsync(new[] { message }, cancellationToken);
    }

    public async Task SaveMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        if (messageList.Count == 0) return;

        using var connection = _context.CreateConnection();
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Comprehensive deduplication statement
        using var checkCmd = connection.CreateCommand();
        checkCmd.Transaction = (SqliteTransaction)transaction;
        checkCmd.CommandText = """
            SELECT id FROM messages
            WHERE account_jid = $account_jid
              AND (
                  id = $id
                  OR ($stanza_id IS NOT NULL AND stanza_id = $stanza_id)
                  OR ($origin_id IS NOT NULL AND origin_id = $origin_id)
                  OR (
                      remote_jid = $remote_jid
                      AND direction = $direction
                      AND body = $body
                      AND timestamp >= $time_min
                      AND timestamp <= $time_max
                  )
              )
            LIMIT 1;
        """;

        var pCheckAccountJid = checkCmd.Parameters.Add("$account_jid", SqliteType.Text);
        var pCheckId = checkCmd.Parameters.Add("$id", SqliteType.Text);
        var pCheckStanzaId = checkCmd.Parameters.Add("$stanza_id", SqliteType.Text);
        var pCheckOriginId = checkCmd.Parameters.Add("$origin_id", SqliteType.Text);
        var pCheckRemoteJid = checkCmd.Parameters.Add("$remote_jid", SqliteType.Text);
        var pCheckDirection = checkCmd.Parameters.Add("$direction", SqliteType.Integer);
        var pCheckBody = checkCmd.Parameters.Add("$body", SqliteType.Text);
        var pCheckTimeMin = checkCmd.Parameters.Add("$time_min", SqliteType.Text);
        var pCheckTimeMax = checkCmd.Parameters.Add("$time_max", SqliteType.Text);

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = (SqliteTransaction)transaction;
        insertCmd.CommandText = """
            INSERT INTO messages (
                id, account_jid, remote_jid, sender_jid, timestamp, direction,
                body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read, raw_xml
            ) VALUES (
                $id, $account_jid, $remote_jid, $sender_jid, $timestamp, $direction,
                $body, $stanza_id, $origin_id, $replace_id, $is_encrypted, $encryption_type, $is_read, $raw_xml
            )
            ON CONFLICT(id) DO UPDATE SET
                body = excluded.body,
                stanza_id = COALESCE(excluded.stanza_id, messages.stanza_id),
                origin_id = COALESCE(excluded.origin_id, messages.origin_id),
                replace_id = excluded.replace_id,
                is_read = excluded.is_read,
                raw_xml = COALESCE(excluded.raw_xml, messages.raw_xml);
        """;

        var pInsertId = insertCmd.Parameters.Add("$id", SqliteType.Text);
        var pInsertAccountJid = insertCmd.Parameters.Add("$account_jid", SqliteType.Text);
        var pInsertRemoteJid = insertCmd.Parameters.Add("$remote_jid", SqliteType.Text);
        var pInsertSenderJid = insertCmd.Parameters.Add("$sender_jid", SqliteType.Text);
        var pInsertTimestamp = insertCmd.Parameters.Add("$timestamp", SqliteType.Text);
        var pInsertDirection = insertCmd.Parameters.Add("$direction", SqliteType.Integer);
        var pInsertBody = insertCmd.Parameters.Add("$body", SqliteType.Text);
        var pInsertStanzaId = insertCmd.Parameters.Add("$stanza_id", SqliteType.Text);
        var pInsertOriginId = insertCmd.Parameters.Add("$origin_id", SqliteType.Text);
        var pInsertReplaceId = insertCmd.Parameters.Add("$replace_id", SqliteType.Text);
        var pInsertIsEncrypted = insertCmd.Parameters.Add("$is_encrypted", SqliteType.Integer);
        var pInsertEncryptionType = insertCmd.Parameters.Add("$encryption_type", SqliteType.Text);
        var pInsertIsRead = insertCmd.Parameters.Add("$is_read", SqliteType.Integer);
        var pInsertRawXml = insertCmd.Parameters.Add("$raw_xml", SqliteType.Text);

        foreach (var message in messageList)
        {
            pCheckAccountJid.Value = message.AccountJid;
            pCheckId.Value = message.Id;
            pCheckStanzaId.Value = (object?)message.StanzaId ?? DBNull.Value;
            pCheckOriginId.Value = (object?)message.OriginId ?? DBNull.Value;
            pCheckRemoteJid.Value = message.RemoteJid;
            pCheckDirection.Value = (int)message.Direction;
            pCheckBody.Value = message.Body;
            pCheckTimeMin.Value = message.Timestamp.AddSeconds(-60).ToString("O");
            pCheckTimeMax.Value = message.Timestamp.AddSeconds(60).ToString("O");

            var existingId = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (existingId is not null)
            {
                message.Id = (string)existingId;
            }

            pInsertId.Value = message.Id;
            pInsertAccountJid.Value = message.AccountJid;
            pInsertRemoteJid.Value = message.RemoteJid;
            pInsertSenderJid.Value = message.SenderJid;
            pInsertTimestamp.Value = message.Timestamp.ToString("O");
            pInsertDirection.Value = (int)message.Direction;
            pInsertBody.Value = message.Body;
            pInsertStanzaId.Value = (object?)message.StanzaId ?? DBNull.Value;
            pInsertOriginId.Value = (object?)message.OriginId ?? DBNull.Value;
            pInsertReplaceId.Value = (object?)message.ReplaceId ?? DBNull.Value;
            pInsertIsEncrypted.Value = message.IsEncrypted ? 1 : 0;
            pInsertEncryptionType.Value = (object?)message.EncryptionType ?? DBNull.Value;
            pInsertIsRead.Value = message.IsRead ? 1 : 0;
            pInsertRawXml.Value = (object?)message.RawXml ?? DBNull.Value;

            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
                       body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read, raw_xml
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
                       body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read, raw_xml
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
                   body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read, raw_xml
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

    public async Task<bool> MarkMessageAsReadAsync(
        string accountJid,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accountJid) || string.IsNullOrEmpty(messageId)) return false;

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            UPDATE messages
            SET is_read = 1
            WHERE account_jid = $account_jid AND (id = $messageId OR stanza_id = $messageId OR origin_id = $messageId);
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$messageId", messageId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<List<string>> MarkUnreadMessagesAsReadAsync(
        string accountJid,
        string remoteJid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accountJid) || string.IsNullOrEmpty(remoteJid)) return [];

        using var connection = _context.CreateConnection();

        // 1. Get IDs of unread inbound messages for this contact
        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = """
            SELECT COALESCE(stanza_id, id)
            FROM messages
            WHERE account_jid = $account_jid AND remote_jid = $remoteJid AND direction = 0 AND is_read = 0;
        """;
        selectCmd.Parameters.AddWithValue("$account_jid", accountJid);
        selectCmd.Parameters.AddWithValue("$remoteJid", remoteJid);

        var markedIds = new List<string>();
        using (var reader = await selectCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    markedIds.Add(reader.GetString(0));
                }
            }
        }

        if (markedIds.Count == 0) return markedIds;

        // 2. Mark them as read in DB
        using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = """
            UPDATE messages
            SET is_read = 1
            WHERE account_jid = $account_jid AND remote_jid = $remoteJid AND direction = 0 AND is_read = 0;
        """;
        updateCmd.Parameters.AddWithValue("$account_jid", accountJid);
        updateCmd.Parameters.AddWithValue("$remoteJid", remoteJid);

        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        return markedIds;
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
            IsRead = reader.GetInt32(12) == 1,
            RawXml = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }
}
