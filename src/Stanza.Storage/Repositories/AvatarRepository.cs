using Microsoft.Data.Sqlite;
using Stanza.Storage.Models;

namespace Stanza.Storage.Repositories;

public sealed class AvatarRepository
{
    private readonly DatabaseContext _context;

    public AvatarRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task SaveAvatarAsync(string jid, string hash, string mimeType, byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jid);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(data);

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO user_avatars (jid, hash, mime_type, data, updated_at)
            VALUES ($jid, $hash, $mime_type, $data, $updated_at)
            ON CONFLICT(jid) DO UPDATE SET
                hash = excluded.hash,
                mime_type = excluded.mime_type,
                data = excluded.data,
                updated_at = excluded.updated_at;
        """;

        cmd.Parameters.AddWithValue("$jid", jid.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$hash", hash.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$mime_type", mimeType);
        cmd.Parameters.AddWithValue("$data", data);
        cmd.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AvatarRecord?> GetAvatarAsync(string jid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jid);

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT jid, hash, mime_type, data, updated_at
            FROM user_avatars
            WHERE jid = $jid;
        """;

        cmd.Parameters.AddWithValue("$jid", jid.Trim().ToLowerInvariant());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadAvatarRecord(reader);
        }

        return null;
    }

    public async Task<AvatarRecord?> GetAvatarByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT jid, hash, mime_type, data, updated_at
            FROM user_avatars
            WHERE hash = $hash
            LIMIT 1;
        """;

        cmd.Parameters.AddWithValue("$hash", hash.Trim().ToLowerInvariant());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadAvatarRecord(reader);
        }

        return null;
    }

    public async Task DeleteAvatarAsync(string jid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jid);

        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "DELETE FROM user_avatars WHERE jid = $jid;";
        cmd.Parameters.AddWithValue("$jid", jid.Trim().ToLowerInvariant());

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, AvatarRecord>> GetAllAvatarsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT jid, hash, mime_type, data, updated_at
            FROM user_avatars;
        """;

        var dict = new Dictionary<string, AvatarRecord>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = ReadAvatarRecord(reader);
            dict[record.Jid] = record;
        }

        return dict;
    }

    private static AvatarRecord ReadAvatarRecord(SqliteDataReader reader)
    {
        var jid = reader.GetString(0);
        var hash = reader.GetString(1);
        var mimeType = reader.GetString(2);
        var data = (byte[])reader[3];
        var updatedAtStr = reader.GetString(4);

        return new AvatarRecord
        {
            Jid = jid,
            Hash = hash,
            MimeType = mimeType,
            Data = data,
            UpdatedAt = DateTimeOffset.TryParse(updatedAtStr, out var dto) ? dto : DateTimeOffset.UtcNow
        };
    }
}
