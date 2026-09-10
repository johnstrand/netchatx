using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetChatx.Storage.Repositories;

public sealed class SettingsRepository
{
    private readonly DatabaseContext _context;

    public static readonly string[] DefaultQuickEmojis = ["👍", "❤️", "😂", "😮", "😢", "🎉"];
    public const string KeyQuickEmojis = "quick_emojis";

    public SettingsRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<string?> GetSettingAsync(string accountJid, string key, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT value FROM account_settings WHERE account_jid = $account_jid AND key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task SetSettingAsync(string accountJid, string key, string value, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO account_settings (account_jid, key, value)
            VALUES ($account_jid, $key, $value)
            ON CONFLICT(account_jid, key) DO UPDATE SET
                value = excluded.value;
        """;

        cmd.Parameters.AddWithValue("$account_jid", accountJid);
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<string>> GetQuickEmojisAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var json = await GetSettingAsync(accountJid, KeyQuickEmojis, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [.. DefaultQuickEmojis];
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is not null && list.Count > 0)
            {
                return list.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            }
        }
        catch
        {
            // Fallback to default on JSON parse failure
        }

        return [.. DefaultQuickEmojis];
    }

    public async Task SetQuickEmojisAsync(string accountJid, IEnumerable<string> emojis, CancellationToken cancellationToken = default)
    {
        var cleanList = emojis.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
        var json = JsonSerializer.Serialize(cleanList);
        await SetSettingAsync(accountJid, KeyQuickEmojis, json, cancellationToken);
    }
}
