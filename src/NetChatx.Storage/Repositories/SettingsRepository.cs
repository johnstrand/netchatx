using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetChatx.Storage.Repositories;

public sealed class SettingsRepository
{
    private readonly DatabaseContext _context;

    public static readonly string[] DefaultQuickEmojis = ["👍", "❤️", "😂", "😮", "😢", "🎉"];
    public const string KeyQuickEmojis = "quick_emojis";
    public const string KeyMergeMessagesEnabled = "merge_messages_enabled";
    public const string KeyMergeMessagesThresholdSeconds = "merge_messages_threshold_seconds";
    public const string KeyNotificationPopupsEnabled = "notification_popups_enabled";
    public const string KeyIconFlashingEnabled = "icon_flashing_enabled";
    public const bool DefaultMergeMessagesEnabled = true;
    public const int DefaultMergeMessagesThresholdSeconds = 10;
    public const bool DefaultNotificationPopupsEnabled = true;
    public const bool DefaultIconFlashingEnabled = true;

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

    public async Task<bool> GetMergeMessagesEnabledAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyMergeMessagesEnabled, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultMergeMessagesEnabled;
    }

    public async Task SetMergeMessagesEnabledAsync(string accountJid, bool enabled, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyMergeMessagesEnabled, enabled.ToString(), cancellationToken);
    }

    public async Task<int> GetMergeMessagesThresholdSecondsAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyMergeMessagesThresholdSeconds, cancellationToken);
        return int.TryParse(val, out var result) ? result : DefaultMergeMessagesThresholdSeconds;
    }

    public async Task SetMergeMessagesThresholdSecondsAsync(string accountJid, int seconds, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyMergeMessagesThresholdSeconds, seconds.ToString(), cancellationToken);
    }

    public async Task<bool> GetNotificationPopupsEnabledAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyNotificationPopupsEnabled, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultNotificationPopupsEnabled;
    }

    public async Task SetNotificationPopupsEnabledAsync(string accountJid, bool enabled, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyNotificationPopupsEnabled, enabled.ToString(), cancellationToken);
    }

    public async Task<bool> GetIconFlashingEnabledAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyIconFlashingEnabled, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultIconFlashingEnabled;
    }

    public async Task SetIconFlashingEnabledAsync(string accountJid, bool enabled, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyIconFlashingEnabled, enabled.ToString(), cancellationToken);
    }
}
