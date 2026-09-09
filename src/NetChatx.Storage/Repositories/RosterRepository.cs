using Microsoft.Data.Sqlite;
using NetChatx.Storage.Models;

namespace NetChatx.Storage.Repositories;

public sealed class RosterRepository
{
    private readonly DatabaseContext _context;

    public RosterRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task UpsertContactAsync(RosterContact contact, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO roster (account_jid, contact_jid, name, subscription, groups)
            VALUES ($account_jid, $contact_jid, $name, $subscription, $groups)
            ON CONFLICT(account_jid, contact_jid) DO UPDATE SET
                name = excluded.name,
                subscription = excluded.subscription,
                groups = excluded.groups;
        """;

        cmd.Parameters.AddWithValue("$account_jid", contact.AccountJid);
        cmd.Parameters.AddWithValue("$contact_jid", contact.ContactJid);
        cmd.Parameters.AddWithValue("$name", (object?)contact.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$subscription", contact.Subscription);
        cmd.Parameters.AddWithValue("$groups", (object?)contact.Groups ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<RosterContact>> GetContactsAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT account_jid, contact_jid, name, subscription, groups
            FROM roster
            WHERE account_jid = $account_jid
            ORDER BY contact_jid ASC;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);

        var list = new List<RosterContact>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RosterContact
            {
                AccountJid = reader.GetString(0),
                ContactJid = reader.GetString(1),
                Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                Subscription = reader.GetString(3),
                Groups = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return list;
    }

    public async Task RemoveContactAsync(string accountJid, string contactJid, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "DELETE FROM roster WHERE account_jid = $account_jid AND contact_jid = $contact_jid;";
        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$contact_jid", contactJid);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
