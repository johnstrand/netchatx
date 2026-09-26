using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Migrations;

public interface IDatabaseMigration
{
    int Version { get; }
    string Description { get; }
    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}
