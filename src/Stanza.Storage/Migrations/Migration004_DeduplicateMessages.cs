using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public sealed class Migration004_DeduplicateMessages : DatabaseMigration
{
    public override int Version => 4;
    public override string Description => "Deduplicate legacy messages table records";

    public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, """
            DELETE FROM messages
            WHERE rowid NOT IN (
                SELECT MIN(rowid)
                FROM messages
                GROUP BY account_jid, remote_jid, COALESCE(stanza_id, id), timestamp, body
            );
            """);
    }
}
