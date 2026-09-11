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
        string targetId,
        string newBody,
        string? replacementStanzaId = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            UPDATE messages
            SET body = $body, replace_id = $replace_id
            WHERE account_jid = $account_jid AND (id = $target_id OR stanza_id = $target_id OR origin_id = $target_id);
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$target_id", targetId);
        cmd.Parameters.AddWithValue("$replace_id", (object?)replacementStanzaId ?? targetId);
        cmd.Parameters.AddWithValue("$body", newBody);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> DeleteMessageAsync(
        string accountJid,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accountJid) || string.IsNullOrEmpty(messageId)) return false;

        using var connection = _context.CreateConnection();
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 1. Delete associated reactions
        using (var delReactCmd = connection.CreateCommand())
        {
            delReactCmd.Transaction = (SqliteTransaction)transaction;
            delReactCmd.CommandText = """
                DELETE FROM message_reactions
                WHERE account_jid = $account_jid AND (message_id = $id OR message_id IN (
                    SELECT id FROM messages WHERE account_jid = $account_jid AND (id = $id OR stanza_id = $id OR origin_id = $id)
                ));
            """;
            delReactCmd.Parameters.AddWithValue("$account_jid", accountJid);
            delReactCmd.Parameters.AddWithValue("$id", messageId);
            await delReactCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Delete message
        using var cmd = connection.CreateCommand();
        cmd.Transaction = (SqliteTransaction)transaction;
        cmd.CommandText = """
            DELETE FROM messages
            WHERE account_jid = $account_jid AND (id = $id OR stanza_id = $id OR origin_id = $id);
        """;
        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$id", messageId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return rows > 0;
    }

    public async Task SaveReactionsAsync(
        string accountJid,
        string remoteJid,
        string targetMessageId,
        string senderJid,
        IEnumerable<string> emojis,
        bool isGroupChat = false,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Normalize sender JID: for 1:1 chats, use bare JID so multiple resources of the same contact/user don't accumulate duplicates
        string normalizedSenderJid = senderJid;
        if (!isGroupChat)
        {
            var slashIdx = senderJid.IndexOf('/');
            if (slashIdx >= 0)
            {
                normalizedSenderJid = senderJid[..slashIdx];
            }
        }

        // Find actual message ID if targetMessageId matches stanza_id or origin_id
        string canonicalMessageId = targetMessageId;
        using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = (SqliteTransaction)transaction;
            findCmd.CommandText = """
                SELECT id FROM messages
                WHERE account_jid = $account_jid
                  AND (id = $target_id OR stanza_id = $target_id OR origin_id = $target_id)
                LIMIT 1;
            """;
            findCmd.Parameters.AddWithValue("$account_jid", accountJid);
            findCmd.Parameters.AddWithValue("$target_id", targetMessageId);

            var found = await findCmd.ExecuteScalarAsync(cancellationToken);
            if (found is string fid)
            {
                canonicalMessageId = fid;
            }
        }

        // Per XEP-0444: sending new reactions replaces sender's previous reactions for that message
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = (SqliteTransaction)transaction;
            deleteCmd.CommandText = """
                DELETE FROM message_reactions
                WHERE account_jid = $account_jid
                  AND remote_jid = $remote_jid
                  AND message_id = $message_id
                  AND (sender_jid = $sender_jid OR sender_jid LIKE $sender_prefix);
            """;
            deleteCmd.Parameters.AddWithValue("$account_jid", accountJid);
            deleteCmd.Parameters.AddWithValue("$remote_jid", remoteJid);
            deleteCmd.Parameters.AddWithValue("$message_id", canonicalMessageId);
            deleteCmd.Parameters.AddWithValue("$sender_jid", normalizedSenderJid);
            deleteCmd.Parameters.AddWithValue("$sender_prefix", normalizedSenderJid + "/%");

            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var emojiList = emojis.Distinct().Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (emojiList.Count > 0)
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = (SqliteTransaction)transaction;
            insertCmd.CommandText = """
                INSERT OR REPLACE INTO message_reactions (account_jid, remote_jid, message_id, sender_jid, emoji)
                VALUES ($account_jid, $remote_jid, $message_id, $sender_jid, $emoji);
            """;

            insertCmd.Parameters.AddWithValue("$account_jid", accountJid);
            insertCmd.Parameters.AddWithValue("$remote_jid", remoteJid);
            insertCmd.Parameters.AddWithValue("$message_id", canonicalMessageId);
            insertCmd.Parameters.AddWithValue("$sender_jid", normalizedSenderJid);
            var pEmoji = insertCmd.Parameters.Add("$emoji", SqliteType.Text);

            foreach (var emoji in emojiList)
            {
                pEmoji.Value = emoji;
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<MessageReaction>> GetReactionsForMessagesAsync(
        string accountJid,
        IEnumerable<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        var idList = messageIds.Distinct().ToList();
        if (idList.Count == 0) return [];

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        var inClause = string.Join(",", idList.Select((_, i) => $"$id{i}"));
        cmd.CommandText = $"""
            SELECT account_jid, remote_jid, message_id, sender_jid, emoji
            FROM message_reactions
            WHERE account_jid = $account_jid AND message_id IN ({inClause});
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        for (int i = 0; i < idList.Count; i++)
        {
            cmd.Parameters.AddWithValue($"$id{i}", idList[i]);
        }

        var list = new List<MessageReaction>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MessageReaction
            {
                AccountJid = reader.GetString(0),
                RemoteJid = reader.GetString(1),
                MessageId = reader.GetString(2),
                SenderJid = reader.GetString(3),
                Emoji = reader.GetString(4)
            });
        }

        return list;
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

    public async Task<DateTimeOffset?> GetLatestMessageTimestampAsync(
        string accountJid,
        string? remoteJid = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accountJid)) return null;

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        if (!string.IsNullOrEmpty(remoteJid))
        {
            cmd.CommandText = "SELECT MAX(timestamp) FROM messages WHERE account_jid = $account_jid AND remote_jid = $remote_jid;";
            cmd.Parameters.AddWithValue("$account_jid", accountJid);
            cmd.Parameters.AddWithValue("$remote_jid", remoteJid);
        }
        else
        {
            cmd.CommandText = "SELECT MAX(timestamp) FROM messages WHERE account_jid = $account_jid;";
            cmd.Parameters.AddWithValue("$account_jid", accountJid);
        }

        var val = await cmd.ExecuteScalarAsync(cancellationToken);
        if (val is string str && DateTimeOffset.TryParse(str, out var dto))
        {
            return dto;
        }

        return null;
    }

    public async Task<Dictionary<string, (int unreadCount, string? lastPreview)>> GetContactSummariesAsync(
        string accountJid,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, (int unreadCount, string? lastPreview)>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(accountJid)) return result;

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT remote_jid,
                   SUM(CASE WHEN direction = 0 AND is_read = 0 THEN 1 ELSE 0 END) AS unread_count,
                   (SELECT body FROM messages m2 WHERE m2.account_jid = m1.account_jid AND m2.remote_jid = m1.remote_jid ORDER BY timestamp DESC LIMIT 1) AS last_body
            FROM messages m1
            WHERE account_jid = $account_jid
            GROUP BY remote_jid;
        """;
        cmd.Parameters.AddWithValue("$account_jid", accountJid);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string remote = reader.GetString(0);
            int unread = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            string? lastBody = reader.IsDBNull(2) ? null : reader.GetString(2);
            result[remote] = (unread, lastBody);
        }

        return result;
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
