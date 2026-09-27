using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public sealed class Migration005_AddAccountLabelAndColorHexColumns : DatabaseMigration
{
    public override int Version => 5;
    public override string Description => "Add label and color_hex columns to accounts table";

    public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!ColumnExists(connection, transaction, "accounts", "label"))
        {
            ExecuteNonQuery(connection, transaction, "ALTER TABLE accounts ADD COLUMN label TEXT;");
        }

        if (!ColumnExists(connection, transaction, "accounts", "color_hex"))
        {
            ExecuteNonQuery(connection, transaction, "ALTER TABLE accounts ADD COLUMN color_hex TEXT;");
        }
    }
}
