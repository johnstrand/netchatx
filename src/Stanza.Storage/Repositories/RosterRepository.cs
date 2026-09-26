using Microsoft.Data.Sqlite;
using Stanza.Storage.Models;

namespace Stanza.Storage.Repositories;

public sealed class RosterRepository
{
    private readonly DatabaseContext _context;

    public RosterRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task UpsertContactAsync(RosterContact contact, CancellationToken cancellationToken = default)
    {
        await UpsertContactsAsync(new[] { contact }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertContactsAsync(IEnumerable<RosterContact> contacts, CancellationToken cancellationToken = default)
    {
        var list = contacts as IReadOnlyCollection<RosterContact> ?? contacts.ToList();
        if (list.Count == 0) return;

        using var connection = _context.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        cmd.CommandText = """
            INSERT INTO roster (account_jid, contact_jid, name, subscription, groups)
            VALUES ($account_jid, $contact_jid, $name, $subscription, $groups)
            ON CONFLICT(account_jid, contact_jid) DO UPDATE SET
                name = excluded.name,
                subscription = excluded.subscription,
                groups = excluded.groups;
        """;

        var pAccountJid = cmd.Parameters.Add("$account_jid", SqliteType.Text);
        var pContactJid = cmd.Parameters.Add("$contact_jid", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pSubscription = cmd.Parameters.Add("$subscription", SqliteType.Text);
        var pGroups = cmd.Parameters.Add("$groups", SqliteType.Text);

        foreach (var contact in list)
        {
            pAccountJid.Value = contact.AccountJid;
            pContactJid.Value = contact.ContactJid;
            pName.Value = (object?)contact.Name ?? DBNull.Value;
            pSubscription.Value = contact.Subscription;
            pGroups.Value = (object?)contact.Groups ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
