using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public sealed class Migration003_AddAllowUntrustedCertificatesColumn : DatabaseMigration
{
    public override int Version => 3;
    public override string Description => "Add allow_untrusted_certificates column to accounts table";

    public override void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!ColumnExists(connection, transaction, "accounts", "allow_untrusted_certificates"))
        {
            ExecuteNonQuery(connection, transaction, "ALTER TABLE accounts ADD COLUMN allow_untrusted_certificates INTEGER NOT NULL DEFAULT 0;");
        }
    }
}
