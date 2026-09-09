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
                body, stanza_id, origin_id, replace_id, is_encrypted, encryption_type, is_read
            ) VALUES (
                $id, $account_jid, $remote_jid, $sender_jid, $timestamp, $direction,
                $body, $stanza_id, $origin_id, $replace_id, $is_encrypted, $encryption_type, $is_read
            )
            ON CONFLICT(id) DO UPDATE SET
                body = excluded.body,
                stanza_id = COALESCE(excluded.stanza_id, messages.stanza_id),
                origin_id = COALESCE(excluded.origin_id, messages.origin_id),
                replace_id = excluded.replace_id,
                is_read = excluded.is_read;
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

    public async Task SaveReactionsAsync(
        string accountJid,
        string remoteJid,
        string targetMessageId,
        string senderJid,
        IEnumerable<string> emojis,
        CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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
                  AND sender_jid = $sender_jid;
            """;
            deleteCmd.Parameters.AddWithValue("$account_jid", accountJid);
            deleteCmd.Parameters.AddWithValue("$remote_jid", remoteJid);
            deleteCmd.Parameters.AddWithValue("$message_id", canonicalMessageId);
            deleteCmd.Parameters.AddWithValue("$sender_jid", senderJid);

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
            insertCmd.Parameters.AddWithValue("$sender_jid", senderJid);
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
