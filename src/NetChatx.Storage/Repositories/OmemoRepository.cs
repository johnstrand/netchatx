using Microsoft.Data.Sqlite;

namespace NetChatx.Storage.Repositories;

public enum OmemoTrustState
{
    Undecided = 0,
    Trusted = 1,
    Untrusted = 2
}

public sealed class OmemoSessionRecord
{
    public required string AccountJid { get; set; }
    public required string RemoteJid { get; set; }
    public int DeviceId { get; set; }
    public required byte[] SessionData { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public OmemoTrustState TrustState { get; set; }
}

public sealed class OmemoRepository
{
    private readonly DatabaseContext _context;

    public OmemoRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task SaveIdentityAsync(string accountJid, int deviceId, string privateKey, string publicKey, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO omemo_identities (account_jid, device_id, identity_key_private, identity_key_public)
            VALUES ($account_jid, $device_id, $private, $public)
            ON CONFLICT(account_jid) DO UPDATE SET
                device_id = excluded.device_id,
                identity_key_private = excluded.identity_key_private,
                identity_key_public = excluded.identity_key_public;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$device_id", deviceId);
        cmd.Parameters.AddWithValue("$private", privateKey);
        cmd.Parameters.AddWithValue("$public", publicKey);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(int DeviceId, string PrivateKey, string PublicKey)?> GetIdentityAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT device_id, identity_key_private, identity_key_public FROM omemo_identities WHERE account_jid = $account_jid;";
        cmd.Parameters.AddWithValue("$account_jid", accountJid);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
        }

        return null;
    }

    public async Task SaveSessionAsync(OmemoSessionRecord session, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO omemo_sessions (account_jid, remote_jid, device_id, session_data, last_active, trust_state)
            VALUES ($account_jid, $remote_jid, $device_id, $session_data, $last_active, $trust_state)
            ON CONFLICT(account_jid, remote_jid, device_id) DO UPDATE SET
                session_data = excluded.session_data,
                last_active = excluded.last_active,
                trust_state = excluded.trust_state;
        """;

        cmd.Parameters.AddWithValue("$account_jid", session.AccountJid);
        cmd.Parameters.AddWithValue("$remote_jid", session.RemoteJid);
        cmd.Parameters.AddWithValue("$device_id", session.DeviceId);
        cmd.Parameters.AddWithValue("$session_data", session.SessionData);
        cmd.Parameters.AddWithValue("$last_active", session.LastActive.ToString("O"));
        cmd.Parameters.AddWithValue("$trust_state", (int)session.TrustState);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OmemoSessionRecord?> GetSessionAsync(string accountJid, string remoteJid, int deviceId, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT session_data, last_active, trust_state
            FROM omemo_sessions
            WHERE account_jid = $account_jid AND remote_jid = $remote_jid AND device_id = $device_id;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$remote_jid", remoteJid);
        cmd.Parameters.AddWithValue("$device_id", deviceId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new OmemoSessionRecord
            {
                AccountJid = accountJid,
                RemoteJid = remoteJid,
                DeviceId = deviceId,
                SessionData = (byte[])reader[0],
                LastActive = DateTimeOffset.Parse(reader.GetString(1)),
                TrustState = (OmemoTrustState)reader.GetInt32(2)
            };
        }

        return null;
    }

    public async Task<List<OmemoSessionRecord>> GetSessionsAsync(string accountJid, string remoteJid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT device_id, session_data, last_active, trust_state
            FROM omemo_sessions
            WHERE account_jid = $account_jid AND remote_jid = $remote_jid;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$remote_jid", remoteJid);

        var list = new List<OmemoSessionRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new OmemoSessionRecord
            {
                AccountJid = accountJid,
                RemoteJid = remoteJid,
                DeviceId = reader.GetInt32(0),
                SessionData = (byte[])reader[1],
                LastActive = DateTimeOffset.Parse(reader.GetString(2)),
                TrustState = (OmemoTrustState)reader.GetInt32(3)
            });
        }

        return list;
    }
}
