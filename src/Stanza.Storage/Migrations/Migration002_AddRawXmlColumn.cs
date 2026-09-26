using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public sealed class Migration002_AddRawXmlColumn : DatabaseMigration
{
    public override int Version => 2;
    public override string Description => "Add raw_xml column to messages table";

    public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!ColumnExists(connection, transaction, "messages", "raw_xml"))
        {
            ExecuteNonQuery(connection, transaction, "ALTER TABLE messages ADD COLUMN raw_xml TEXT;");
        }
    }
}
