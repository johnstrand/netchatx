using Microsoft.Data.Sqlite;
using Stanza.Storage.Models;

namespace Stanza.Storage.Repositories;

public sealed class AccountRepository
{
    private readonly DatabaseContext _context;

    public AccountRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task SaveAccountAsync(AccountProfile account, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO accounts (jid, password, resource, host, port, use_direct_tls, is_active)
            VALUES ($jid, $password, $resource, $host, $port, $use_direct_tls, $is_active)
            ON CONFLICT(jid) DO UPDATE SET
                password = excluded.password,
                resource = excluded.resource,
                host = excluded.host,
                port = excluded.port,
                use_direct_tls = excluded.use_direct_tls,
                is_active = excluded.is_active;
        """;

        cmd.Parameters.AddWithValue("$jid", account.Jid);
        cmd.Parameters.AddWithValue("$password", account.Password);
        cmd.Parameters.AddWithValue("$resource", account.Resource);
        cmd.Parameters.AddWithValue("$host", (object?)account.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$port", account.Port);
        cmd.Parameters.AddWithValue("$use_direct_tls", account.UseDirectTls ? 1 : 0);
        cmd.Parameters.AddWithValue("$is_active", account.IsActive ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT jid, password, resource, host, port, use_direct_tls, is_active FROM accounts;";

        var list = new List<AccountProfile>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new AccountProfile
            {
                Jid = reader.GetString(0),
                Password = reader.GetString(1),
                Resource = reader.GetString(2),
                Host = reader.IsDBNull(3) ? null : reader.GetString(3),
                Port = reader.GetInt32(4),
                UseDirectTls = reader.GetInt32(5) == 1,
                IsActive = reader.GetInt32(6) == 1
            });
        }

        return list;
    }

    public async Task<AccountProfile?> GetAccountAsync(string jid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT jid, password, resource, host, port, use_direct_tls, is_active FROM accounts WHERE jid = $jid;";
        cmd.Parameters.AddWithValue("$jid", jid);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new AccountProfile
            {
                Jid = reader.GetString(0),
                Password = reader.GetString(1),
                Resource = reader.GetString(2),
                Host = reader.IsDBNull(3) ? null : reader.GetString(3),
                Port = reader.GetInt32(4),
                UseDirectTls = reader.GetInt32(5) == 1,
                IsActive = reader.GetInt32(6) == 1
            };
        }

        return null;
    }

    public async Task DeleteAccountAsync(string jid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "DELETE FROM accounts WHERE jid = $jid;";
        cmd.Parameters.AddWithValue("$jid", jid);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
