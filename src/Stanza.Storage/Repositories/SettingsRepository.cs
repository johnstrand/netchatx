using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Repositories;

[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<EmoticonMapping>))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}

public sealed record EmoticonMapping(string Shortcut, string Emoji);

public sealed class SettingsRepository
{
    private readonly DatabaseContext _context;

    public static readonly string[] DefaultQuickEmojis = ["👍", "❤️", "😂", "😮", "😢", "🎉"];

    public static readonly List<EmoticonMapping> DefaultEmoticonMappings =
    [
        new(":-)", "🙂"),
        new(":)", "🙂"),
        new(":-(", "🙁"),
        new(":(", "🙁"),
        new(";-)", "😉"),
        new(";)", "😉"),
        new(":-D", "😃"),
        new(":D", "😃"),
        new(":-P", "😛"),
        new(":P", "😛"),
        new(":-O", "😮"),
        new(":O", "😮"),
        new("<3", "❤️"),
        new("8)", "😎")
    ];

    public const string KeyQuickEmojis = "quick_emojis";
    public const string KeyAutoReplaceEmoticons = "auto_replace_emoticons";
    public const string KeyEmoticonMappings = "emoticon_mappings";
    public const string KeyEmoticonBannerDismissed = "emoticon_banner_dismissed";
    public const string KeyMergeMessagesEnabled = "merge_messages_enabled";
    public const string KeyMergeMessagesThresholdSeconds = "merge_messages_threshold_seconds";
    public const string KeyNotificationPopupsEnabled = "notification_popups_enabled";
    public const string KeyIconFlashingEnabled = "icon_flashing_enabled";
    public const string KeyLaunchOnStartup = "launch_on_startup";
    public const string KeyFontFamily = "font_family";
    public const string KeyFontSize = "font_size";
    public const string KeySendOnEnter = "send_on_enter";
    public const string KeyUse24HourClock = "use_24h_clock";
    public const string KeyShowInlinePreviews = "show_inline_previews";
    public const string KeyAutoDownloadMedia = "auto_download_media";
    public const string KeyThemeMode = "theme_mode";
    public const string KeyAccentColor = "accent_color";
    public const string KeyOutboundBubbleColor = "outbound_bubble_color";
    public const string KeyInboundBubbleColor = "inbound_bubble_color";
    public const string KeyOutboundBubbleTextColor = "outbound_bubble_text_color";
    public const string KeyInboundBubbleTextColor = "inbound_bubble_text_color";
    public const string KeyChatInputMaxLines = "chat_input_max_lines";
    public const string KeyLastActiveChat = "last_active_chat";
    public const string KeyLastPresenceMode = "last_presence_mode";
    public const string KeyLastStatusMessage = "last_status_message";
    public const string KeyCloseAction = "close_action";

    public const bool DefaultMergeMessagesEnabled = true;
    public const int DefaultMergeMessagesThresholdSeconds = 10;
    public const bool DefaultAutoReplaceEmoticons = true;
    public const bool DefaultEmoticonBannerDismissed = false;
    public const bool DefaultNotificationPopupsEnabled = true;
    public const bool DefaultIconFlashingEnabled = true;
    public const bool DefaultLaunchOnStartup = false;
    public const string DefaultFontFamily = "Inter";
    public const double DefaultFontSize = 13.0;
    public const bool DefaultSendOnEnter = true;
    public const bool DefaultUse24HourClock = true;
    public const bool DefaultShowInlinePreviews = true;
    public const bool DefaultAutoDownloadMedia = true;
    public const string DefaultThemeMode = "Dark";
    public const string DefaultAccentColor = "#00F0FF";
    public const string DefaultOutboundBubbleColor = "#2563EB";
    public const string DefaultInboundBubbleColor = "#1E293B";
    public const string DefaultOutboundBubbleTextColor = "#FFFFFF";
    public const string DefaultInboundBubbleTextColor = "#F1F5F9";
    public const int DefaultChatInputMaxLines = 5;
    public const string DefaultPresenceMode = "available";
    public const string DefaultStatusMessage = "Online with Stanza";
    public const string DefaultCloseAction = "Ask";

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
            var list = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListString);
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
        var json = JsonSerializer.Serialize(cleanList, SettingsJsonContext.Default.ListString);
        await SetSettingAsync(accountJid, KeyQuickEmojis, json, cancellationToken);
    }

    public async Task<bool> GetAutoReplaceEmoticonsAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyAutoReplaceEmoticons, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultAutoReplaceEmoticons;
    }

    public async Task SetAutoReplaceEmoticonsAsync(string accountJid, bool enabled, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyAutoReplaceEmoticons, enabled.ToString(), cancellationToken);
    }

    public async Task<bool> GetEmoticonBannerDismissedAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyEmoticonBannerDismissed, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultEmoticonBannerDismissed;
    }

    public async Task SetEmoticonBannerDismissedAsync(string accountJid, bool dismissed, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyEmoticonBannerDismissed, dismissed.ToString(), cancellationToken);
    }

    public async Task<List<EmoticonMapping>> GetEmoticonMappingsAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var json = await GetSettingAsync(accountJid, KeyEmoticonMappings, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [.. DefaultEmoticonMappings];
        }

        try
        {
            var list = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListEmoticonMapping);
            if (list is not null && list.Count > 0)
            {
                return list.Where(m => !string.IsNullOrWhiteSpace(m.Shortcut) && !string.IsNullOrWhiteSpace(m.Emoji)).ToList();
            }
        }
        catch
        {
            // Fallback to default on JSON parse failure
        }

        return [.. DefaultEmoticonMappings];
    }

    public async Task SetEmoticonMappingsAsync(string accountJid, IEnumerable<EmoticonMapping> mappings, CancellationToken cancellationToken = default)
    {
        var cleanList = mappings.Where(m => !string.IsNullOrWhiteSpace(m.Shortcut) && !string.IsNullOrWhiteSpace(m.Emoji)).ToList();
        var json = JsonSerializer.Serialize(cleanList, SettingsJsonContext.Default.ListEmoticonMapping);
        await SetSettingAsync(accountJid, KeyEmoticonMappings, json, cancellationToken);
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

    public async Task<bool> GetLaunchOnStartupAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyLaunchOnStartup, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultLaunchOnStartup;
    }

    public async Task SetLaunchOnStartupAsync(string accountJid, bool enabled, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyLaunchOnStartup, enabled.ToString(), cancellationToken);
    }

    public async Task<string> GetFontFamilyAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyFontFamily, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultFontFamily;
    }

    public async Task SetFontFamilyAsync(string accountJid, string fontFamily, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyFontFamily, fontFamily ?? DefaultFontFamily, cancellationToken);
    }

    public async Task<double> GetFontSizeAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyFontSize, cancellationToken);
        return double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : DefaultFontSize;
    }

    public async Task SetFontSizeAsync(string accountJid, double fontSize, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyFontSize, fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
    }

    public async Task<bool> GetSendOnEnterAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeySendOnEnter, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultSendOnEnter;
    }

    public async Task SetSendOnEnterAsync(string accountJid, bool sendOnEnter, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeySendOnEnter, sendOnEnter.ToString(), cancellationToken);
    }

    public async Task<bool> GetUse24HourClockAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyUse24HourClock, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultUse24HourClock;
    }

    public async Task SetUse24HourClockAsync(string accountJid, bool use24H, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyUse24HourClock, use24H.ToString(), cancellationToken);
    }

    public async Task<bool> GetShowInlinePreviewsAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyShowInlinePreviews, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultShowInlinePreviews;
    }

    public async Task SetShowInlinePreviewsAsync(string accountJid, bool show, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyShowInlinePreviews, show.ToString(), cancellationToken);
    }

    public async Task<bool> GetAutoDownloadMediaAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyAutoDownloadMedia, cancellationToken);
        return bool.TryParse(val, out var result) ? result : DefaultAutoDownloadMedia;
    }

    public async Task SetAutoDownloadMediaAsync(string accountJid, bool autoDownload, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyAutoDownloadMedia, autoDownload.ToString(), cancellationToken);
    }

    public async Task<string> GetThemeModeAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyThemeMode, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultThemeMode;
    }

    public async Task SetThemeModeAsync(string accountJid, string themeMode, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyThemeMode, themeMode ?? DefaultThemeMode, cancellationToken);
    }

    public async Task<string> GetAccentColorAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyAccentColor, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultAccentColor;
    }

    public async Task SetAccentColorAsync(string accountJid, string accentColor, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyAccentColor, accentColor ?? DefaultAccentColor, cancellationToken);
    }

    public async Task<string> GetOutboundBubbleColorAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyOutboundBubbleColor, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultOutboundBubbleColor;
    }

    public async Task SetOutboundBubbleColorAsync(string accountJid, string colorHex, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyOutboundBubbleColor, colorHex ?? DefaultOutboundBubbleColor, cancellationToken);
    }

    public async Task<string> GetInboundBubbleColorAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyInboundBubbleColor, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultInboundBubbleColor;
    }

    public async Task SetInboundBubbleColorAsync(string accountJid, string colorHex, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyInboundBubbleColor, colorHex ?? DefaultInboundBubbleColor, cancellationToken);
    }

    public async Task<string> GetOutboundBubbleTextColorAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyOutboundBubbleTextColor, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultOutboundBubbleTextColor;
    }

    public async Task SetOutboundBubbleTextColorAsync(string accountJid, string colorHex, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyOutboundBubbleTextColor, colorHex ?? DefaultOutboundBubbleTextColor, cancellationToken);
    }

    public async Task<string> GetInboundBubbleTextColorAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyInboundBubbleTextColor, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultInboundBubbleTextColor;
    }

    public async Task SetInboundBubbleTextColorAsync(string accountJid, string colorHex, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyInboundBubbleTextColor, colorHex ?? DefaultInboundBubbleTextColor, cancellationToken);
    }

    public async Task<int> GetChatInputMaxLinesAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyChatInputMaxLines, cancellationToken);
        if (int.TryParse(val, out int parsed))
        {
            return Math.Clamp(parsed, 1, 20);
        }
        return DefaultChatInputMaxLines;
    }

    public async Task SetChatInputMaxLinesAsync(string accountJid, int maxLines, CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(maxLines, 1, 20);
        await SetSettingAsync(accountJid, KeyChatInputMaxLines, clamped.ToString(), cancellationToken);
    }

    public async Task<string?> GetLastActiveChatAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        return await GetSettingAsync(accountJid, KeyLastActiveChat, cancellationToken);
    }

    public async Task SetLastActiveChatAsync(string accountJid, string? chatJid, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyLastActiveChat, chatJid ?? string.Empty, cancellationToken);
    }

    public async Task<string> GetLastPresenceModeAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyLastPresenceMode, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultPresenceMode;
    }

    public async Task SetLastPresenceModeAsync(string accountJid, string presenceMode, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyLastPresenceMode, !string.IsNullOrWhiteSpace(presenceMode) ? presenceMode : DefaultPresenceMode, cancellationToken);
    }

    public async Task<string> GetLastStatusMessageAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyLastStatusMessage, cancellationToken);
        return val is not null ? val : DefaultStatusMessage;
    }

    public async Task SetLastStatusMessageAsync(string accountJid, string statusMessage, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyLastStatusMessage, statusMessage ?? DefaultStatusMessage, cancellationToken);
    }

    public async Task<string> GetCloseActionAsync(string accountJid, CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync(accountJid, KeyCloseAction, cancellationToken);
        return !string.IsNullOrWhiteSpace(val) ? val : DefaultCloseAction;
    }

    public async Task SetCloseActionAsync(string accountJid, string closeAction, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync(accountJid, KeyCloseAction, !string.IsNullOrWhiteSpace(closeAction) ? closeAction : DefaultCloseAction, cancellationToken);
    }
}
