using Microsoft.Data.Sqlite;
using Stanza.Storage.Models;
using Stanza.Storage.Security;

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
            INSERT INTO accounts (jid, password, resource, host, port, use_direct_tls, allow_untrusted_certificates, is_active, label, color_hex)
            VALUES ($jid, $password, $resource, $host, $port, $use_direct_tls, $allow_untrusted_certificates, $is_active, $label, $color_hex)
            ON CONFLICT(jid) DO UPDATE SET
                password = excluded.password,
                resource = excluded.resource,
                host = excluded.host,
                port = excluded.port,
                use_direct_tls = excluded.use_direct_tls,
                allow_untrusted_certificates = excluded.allow_untrusted_certificates,
                is_active = excluded.is_active,
                label = excluded.label,
                color_hex = excluded.color_hex;
        """;

        cmd.Parameters.AddWithValue("$jid", account.Jid);
        cmd.Parameters.AddWithValue("$password", SecretProtection.ProtectText(account.Password, _context.SecretProtector));
        cmd.Parameters.AddWithValue("$resource", account.Resource);
        cmd.Parameters.AddWithValue("$host", (object?)account.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$port", account.Port);
        cmd.Parameters.AddWithValue("$use_direct_tls", account.UseDirectTls ? 1 : 0);
        cmd.Parameters.AddWithValue("$allow_untrusted_certificates", account.AllowUntrustedCertificates ? 1 : 0);
        cmd.Parameters.AddWithValue("$is_active", account.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$label", (object?)account.Label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$color_hex", (object?)account.ColorHex ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT jid, password, resource, host, port, use_direct_tls, allow_untrusted_certificates, is_active, label, color_hex FROM accounts;";

        var list = new List<AccountProfile>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new AccountProfile
            {
                Jid = reader.GetString(0),
                Password = SecretProtection.UnprotectText(reader.GetString(1), _context.SecretProtector),
                Resource = reader.GetString(2),
                Host = reader.IsDBNull(3) ? null : reader.GetString(3),
                Port = reader.GetInt32(4),
                UseDirectTls = reader.GetInt32(5) == 1,
                AllowUntrustedCertificates = reader.GetInt32(6) == 1,
                IsActive = reader.GetInt32(7) == 1,
                Label = reader.IsDBNull(8) ? null : reader.GetString(8),
                ColorHex = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return list;
    }

    public async Task<AccountProfile?> GetAccountAsync(string jid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT jid, password, resource, host, port, use_direct_tls, allow_untrusted_certificates, is_active, label, color_hex FROM accounts WHERE jid = $jid;";
        cmd.Parameters.AddWithValue("$jid", jid);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new AccountProfile
            {
                Jid = reader.GetString(0),
                Password = SecretProtection.UnprotectText(reader.GetString(1), _context.SecretProtector),
                Resource = reader.GetString(2),
                Host = reader.IsDBNull(3) ? null : reader.GetString(3),
                Port = reader.GetInt32(4),
                UseDirectTls = reader.GetInt32(5) == 1,
                AllowUntrustedCertificates = reader.GetInt32(6) == 1,
                IsActive = reader.GetInt32(7) == 1,
                Label = reader.IsDBNull(8) ? null : reader.GetString(8),
                ColorHex = reader.IsDBNull(9) ? null : reader.GetString(9)
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

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
