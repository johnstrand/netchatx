using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Storage.Export;

/// <summary>
/// Provides comprehensive message export, backup, and restore capabilities.
/// Supports JSON (round-trip backup), Plain Text, and styled HTML formats.
/// </summary>
public sealed partial class MessageExportService
{
    private readonly MessageRepository _messageRepo;

    public MessageExportService(MessageRepository messageRepo)
    {
        _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
    }

    [GeneratedRegex(@"https?://[^\s<>""'()]+", RegexOptions.IgnoreCase)]
    private static partial Regex HttpUrlRegex();

    [GeneratedRegex(@"\.(png|jpg|jpeg|gif|webp|svg)(\?[^\s<>""'()]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageUrlRegex();

    public async Task ExportConversationAsync(
        string accountJid,
        string remoteJid,
        MessageExportOptions options,
        Stream destinationStream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destinationStream);
        var content = await ExportConversationToStringAsync(accountJid, remoteJid, options, ct).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(content);
        await destinationStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await destinationStream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> ExportConversationToStringAsync(
        string accountJid,
        string remoteJid,
        MessageExportOptions options,
        CancellationToken ct = default)
    {
        var messages = await _messageRepo.GetAllMessagesForConversationAsync(accountJid, remoteJid, ct).ConfigureAwait(false);
        var filtered = FilterMessages(messages, options);

        return options.Format switch
        {
            MessageExportFormat.Json => ExportToJson(filtered, accountJid, remoteJid, options),
            MessageExportFormat.PlainText => ExportToPlainText(filtered, accountJid, remoteJid, options),
            MessageExportFormat.Html => ExportToHtml(filtered, accountJid, remoteJid, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported format: {options.Format}")
        };
    }

    public async Task ExportAllMessagesAsync(
        MessageExportOptions options,
        Stream destinationStream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destinationStream);
        var content = await ExportAllMessagesToStringAsync(options, ct).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(content);
        await destinationStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await destinationStream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> ExportAllMessagesToStringAsync(
        MessageExportOptions options,
        CancellationToken ct = default)
    {
        var messages = await _messageRepo.GetAllMessagesAsync(options.AccountJid, ct).ConfigureAwait(false);
        var filtered = FilterMessages(messages, options);

        return options.Format switch
        {
            MessageExportFormat.Json => ExportToJson(filtered, options.AccountJid, options.RemoteJid, options),
            MessageExportFormat.PlainText => ExportToPlainText(filtered, options.AccountJid, options.RemoteJid, options),
            MessageExportFormat.Html => ExportToHtml(filtered, options.AccountJid, options.RemoteJid, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported format: {options.Format}")
        };
    }

    public async Task<MessageImportResult> ImportBackupAsync(
        Stream sourceStream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        using var reader = new StreamReader(sourceStream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return await ImportBackupFromJsonAsync(json, ct).ConfigureAwait(false);
    }

    public async Task<MessageImportResult> ImportBackupFromJsonAsync(
        string json,
        CancellationToken ct = default)
    {
        var result = new MessageImportResult();

        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add("Backup document is empty.");
            return result;
        }

        MessageBackupDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ExportJsonContext.Default.MessageBackupDocument);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Invalid JSON backup format: {ex.Message}");
            return result;
        }

        if (document is null || document.Messages is null)
        {
            result.Errors.Add("Backup document does not contain messages.");
            return result;
        }

        result.TotalInBackup = document.Messages.Count;
        var chatMessages = new List<ChatMessage>(document.Messages.Count);

        foreach (var item in document.Messages)
        {
            if (string.IsNullOrWhiteSpace(item.AccountJid) || string.IsNullOrWhiteSpace(item.RemoteJid))
            {
                result.SkippedCount++;
                continue;
            }

            var dir = string.Equals(item.Direction, "outbound", StringComparison.OrdinalIgnoreCase)
                ? MessageDirection.Outbound
                : MessageDirection.Inbound;

            var msg = new ChatMessage
            {
                Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                AccountJid = item.AccountJid,
                RemoteJid = item.RemoteJid,
                SenderJid = !string.IsNullOrWhiteSpace(item.SenderJid) ? item.SenderJid : item.AccountJid,
                Timestamp = item.Timestamp != default ? item.Timestamp : DateTimeOffset.UtcNow,
                Direction = dir,
                Body = item.Body ?? string.Empty,
                StanzaId = item.StanzaId,
                OriginId = item.OriginId,
                ReplaceId = item.ReplaceId,
                IsEncrypted = item.IsEncrypted,
                EncryptionType = item.EncryptionType,
                IsRead = item.IsRead,
                RawXml = item.RawXml
            };

            chatMessages.Add(msg);
        }

        if (chatMessages.Count > 0)
        {
            try
            {
                await _messageRepo.SaveMessagesAsync(chatMessages, ct).ConfigureAwait(false);
                result.ImportedCount = chatMessages.Count;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to persist messages to database: {ex.Message}");
            }
        }

        return result;
    }

    private static List<ChatMessage> FilterMessages(List<ChatMessage> messages, MessageExportOptions options)
    {
        IEnumerable<ChatMessage> query = messages;

        if (options.FromDate.HasValue)
        {
            query = query.Where(m => m.Timestamp >= options.FromDate.Value);
        }

        if (options.ToDate.HasValue)
        {
            query = query.Where(m => m.Timestamp <= options.ToDate.Value);
        }

        return query.ToList();
    }

    private static string ExportToJson(
        List<ChatMessage> messages,
        string? accountJid,
        string? remoteJid,
        MessageExportOptions options)
    {
        var doc = new MessageBackupDocument
        {
            Version = 1,
            Client = "Stanza",
            ClientVersion = "1.0.0",
            ExportedAt = DateTimeOffset.UtcNow,
            AccountJid = accountJid,
            RemoteJid = remoteJid,
            TotalMessages = messages.Count,
            Messages = messages.Select(m => ToBackupItem(m, options)).ToList()
        };

        return JsonSerializer.Serialize(doc, ExportJsonContext.Default.MessageBackupDocument);
    }

    private static MessageBackupItem ToBackupItem(ChatMessage m, MessageExportOptions options)
    {
        List<string>? mediaUrls = null;
        if (options.IncludeMedia)
        {
            var urls = ExtractUrls(m.Body);
            if (urls.Count > 0)
            {
                mediaUrls = urls;
            }
        }

        return new MessageBackupItem
        {
            Id = m.Id,
            AccountJid = m.AccountJid,
            RemoteJid = m.RemoteJid,
            SenderJid = m.SenderJid,
            Timestamp = m.Timestamp,
            Direction = m.Direction == MessageDirection.Outbound ? "outbound" : "inbound",
            Body = m.Body,
            StanzaId = options.IncludeMetadata ? m.StanzaId : null,
            OriginId = options.IncludeMetadata ? m.OriginId : null,
            ReplaceId = options.IncludeMetadata ? m.ReplaceId : null,
            IsEncrypted = m.IsEncrypted,
            EncryptionType = m.EncryptionType,
            IsRead = m.IsRead,
            RawXml = options.IncludeMetadata ? m.RawXml : null,
            MediaUrls = mediaUrls
        };
    }

    private static string ExportToPlainText(
        List<ChatMessage> messages,
        string? accountJid,
        string? remoteJid,
        MessageExportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("Stanza Message History Export");
        if (!string.IsNullOrEmpty(accountJid))
        {
            sb.AppendLine($"Account: {accountJid}");
        }
        if (!string.IsNullOrEmpty(remoteJid))
        {
            sb.AppendLine($"Conversation: {remoteJid}");
        }
        sb.AppendLine($"Exported At: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Total Messages: {messages.Count}");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        DateTimeOffset? currentDate = null;

        foreach (var msg in messages)
        {
            var msgDate = msg.Timestamp.ToLocalTime().Date;
            if (currentDate != msgDate)
            {
                currentDate = msgDate;
                sb.AppendLine($"--- {msgDate:D} ---");
                sb.AppendLine();
            }

            var timeStr = msg.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var sender = msg.Direction == MessageDirection.Outbound ? "Me" : msg.SenderJid;

            var encInfo = string.Empty;
            if (options.IncludeMetadata && msg.IsEncrypted)
            {
                encInfo = $" [🔒 {msg.EncryptionType ?? "Encrypted"}]";
            }

            sb.AppendLine($"[{timeStr}] <{sender}>{encInfo}:");
            sb.AppendLine(msg.Body);

            if (options.IncludeMedia)
            {
                var urls = ExtractUrls(msg.Body);
                if (urls.Count > 0)
                {
                    sb.AppendLine($"  [Media links: {string.Join(", ", urls)}]");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ExportToHtml(
        List<ChatMessage> messages,
        string? accountJid,
        string? remoteJid,
        MessageExportOptions options)
    {
        var title = !string.IsNullOrEmpty(remoteJid)
            ? $"Chat with {remoteJid}"
            : "All Conversations";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{WebUtility.HtmlEncode(title)} - Stanza Export</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root {");
        sb.AppendLine("      --bg: #080C14;");
        sb.AppendLine("      --card-bg: #0F172A;");
        sb.AppendLine("      --border: #1E293B;");
        sb.AppendLine("      --accent: #00F0FF;");
        sb.AppendLine("      --text-main: #F8FAFC;");
        sb.AppendLine("      --text-muted: #94A3B8;");
        sb.AppendLine("      --outbound-bg: #2563EB;");
        sb.AppendLine("      --inbound-bg: #1E293B;");
        sb.AppendLine("    }");
        sb.AppendLine("    * { box-sizing: border-box; margin: 0; padding: 0; }");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: var(--bg); color: var(--text-main); line-height: 1.5; padding: 24px 16px; }");
        sb.AppendLine("    .container { max-width: 820px; margin: 0 auto; background: var(--card-bg); border: 1px solid var(--border); border-radius: 16px; box-shadow: 0 12px 40px rgba(0,0,0,0.5), 0 0 20px rgba(0,240,255,0.08); overflow: hidden; }");
        sb.AppendLine("    .header { padding: 24px; border-bottom: 1px solid var(--border); background: linear-gradient(135deg, #151D33 0%, #0F172A 100%); }");
        sb.AppendLine("    .header h1 { font-size: 20px; font-weight: 700; color: var(--text-main); margin-bottom: 4px; display: flex; align-items: center; gap: 8px; }");
        sb.AppendLine("    .header .subtitle { font-size: 13px; color: var(--text-muted); }");
        sb.AppendLine("    .header .badges { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }");
        sb.AppendLine("    .badge { font-size: 11px; font-weight: 600; padding: 4px 10px; border-radius: 9999px; background: rgba(0,240,255,0.12); color: var(--accent); border: 1px solid rgba(0,240,255,0.25); }");
        sb.AppendLine("    .messages-list { padding: 24px; display: flex; flex-direction: column; gap: 12px; }");
        sb.AppendLine("    .date-divider { text-align: center; margin: 16px 0 8px 0; position: relative; }");
        sb.AppendLine("    .date-divider::before { content: ''; position: absolute; left: 0; top: 50%; width: 100%; height: 1px; background: var(--border); z-index: 1; }");
        sb.AppendLine("    .date-divider span { position: relative; z-index: 2; background: var(--card-bg); padding: 4px 14px; font-size: 11px; font-weight: 600; color: var(--text-muted); border: 1px solid var(--border); border-radius: 12px; text-transform: uppercase; letter-spacing: 0.5px; }");
        sb.AppendLine("    .message-row { display: flex; flex-direction: column; }");
        sb.AppendLine("    .message-row.outbound { align-items: flex-end; }");
        sb.AppendLine("    .message-row.inbound { align-items: flex-start; }");
        sb.AppendLine("    .bubble { max-width: 75%; padding: 10px 14px; border-radius: 14px; word-break: break-word; font-size: 14px; }");
        sb.AppendLine("    .message-row.outbound .bubble { background: var(--outbound-bg); color: #FFFFFF; border-bottom-right-radius: 3px; }");
        sb.AppendLine("    .message-row.inbound .bubble { background: var(--inbound-bg); color: var(--text-main); border-bottom-left-radius: 3px; border: 1px solid var(--border); }");
        sb.AppendLine("    .meta { font-size: 11px; margin-top: 4px; color: var(--text-muted); display: flex; align-items: center; gap: 6px; }");
        sb.AppendLine("    .sender { font-weight: 600; font-size: 11px; margin-bottom: 2px; color: var(--accent); }");
        sb.AppendLine("    .enc-badge { font-size: 10px; padding: 1px 6px; border-radius: 4px; background: rgba(16,185,129,0.15); color: #34D399; font-weight: 600; }");
        sb.AppendLine("    .media-preview { max-width: 100%; max-height: 280px; border-radius: 8px; margin-top: 8px; display: block; }");
        sb.AppendLine("    a { color: var(--accent); text-decoration: none; }");
        sb.AppendLine("    a:hover { text-decoration: underline; }");
        sb.AppendLine("    .footer { padding: 16px 24px; text-align: center; font-size: 12px; color: var(--text-muted); border-top: 1px solid var(--border); }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"container\">");
        sb.AppendLine("    <div class=\"header\">");
        sb.AppendLine($"      <h1>💬 {WebUtility.HtmlEncode(title)}</h1>");
        if (!string.IsNullOrEmpty(accountJid))
        {
            sb.AppendLine($"      <div class=\"subtitle\">Account: {WebUtility.HtmlEncode(accountJid)}</div>");
        }
        sb.AppendLine("      <div class=\"badges\">");
        sb.AppendLine($"        <span class=\"badge\">Exported: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>");
        sb.AppendLine($"        <span class=\"badge\">Messages: {messages.Count}</span>");
        sb.AppendLine("        <span class=\"badge\">Client: Stanza</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"messages-list\">");

        DateTimeOffset? currentDate = null;

        foreach (var msg in messages)
        {
            var msgDate = msg.Timestamp.ToLocalTime().Date;
            if (currentDate != msgDate)
            {
                currentDate = msgDate;
                sb.AppendLine($"      <div class=\"date-divider\"><span>{msgDate:D}</span></div>");
            }

            var isOutbound = msg.Direction == MessageDirection.Outbound;
            var dirClass = isOutbound ? "outbound" : "inbound";
            var timeStr = msg.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var senderDisplay = isOutbound ? "You" : msg.SenderJid;

            sb.AppendLine($"      <div class=\"message-row {dirClass}\">");
            if (!isOutbound)
            {
                sb.AppendLine($"        <div class=\"sender\">{WebUtility.HtmlEncode(senderDisplay)}</div>");
            }

            sb.Append("        <div class=\"bubble\">");
            sb.Append(FormatHtmlBody(msg.Body, options.IncludeMedia));
            sb.AppendLine("</div>");

            sb.Append("        <div class=\"meta\">");
            sb.Append($"<span>{timeStr}</span>");
            if (options.IncludeMetadata && msg.IsEncrypted)
            {
                sb.Append($"<span class=\"enc-badge\">🔒 {WebUtility.HtmlEncode(msg.EncryptionType ?? "OMEMO")}</span>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("      </div>");
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"footer\">");
        sb.AppendLine("      Generated by <strong>Stanza</strong> &bull; Modern Cross-Platform XMPP Client");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string FormatHtmlBody(string body, bool includeMedia)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        var encoded = WebUtility.HtmlEncode(body).Replace("\r\n", "<br/>").Replace("\n", "<br/>");

        if (includeMedia)
        {
            var matches = HttpUrlRegex().Matches(body);
            var sbMedia = new StringBuilder();
            foreach (Match match in matches)
            {
                var url = match.Value;
                if (ImageUrlRegex().IsMatch(url))
                {
                    var safeUrl = WebUtility.HtmlEncode(url);
                    sbMedia.Append($"<br/><a href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener noreferrer\"><img src=\"{safeUrl}\" class=\"media-preview\" alt=\"Media preview\" /></a>");
                }
            }
            if (sbMedia.Length > 0)
            {
                encoded += sbMedia.ToString();
            }
        }

        return encoded;
    }

    private static List<string> ExtractUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var matches = HttpUrlRegex().Matches(text);
        return matches.Select(m => m.Value).Distinct().ToList();
    }
}
