using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stanza.Storage.Export;

public enum MessageExportFormat
{
    Json = 0,
    PlainText = 1,
    Html = 2
}

public sealed class MessageExportOptions
{
    public MessageExportFormat Format { get; set; } = MessageExportFormat.Json;
    public bool IncludeMedia { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
    public string? AccountJid { get; set; }
    public string? RemoteJid { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
}

public sealed class MessageBackupItem
{
    public string Id { get; set; } = string.Empty;
    public string AccountJid { get; set; } = string.Empty;
    public string RemoteJid { get; set; } = string.Empty;
    public string SenderJid { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Direction { get; set; } = "inbound";
    public string Body { get; set; } = string.Empty;
    public string? StanzaId { get; set; }
    public string? OriginId { get; set; }
    public string? ReplaceId { get; set; }
    public bool IsEncrypted { get; set; }
    public string? EncryptionType { get; set; }
    public bool IsRead { get; set; }
    public string? RawXml { get; set; }
    public List<string>? MediaUrls { get; set; }
}

public sealed class MessageBackupDocument
{
    public int Version { get; set; } = 1;
    public string Client { get; set; } = "Stanza";
    public string ClientVersion { get; set; } = "1.0.0";
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AccountJid { get; set; }
    public string? RemoteJid { get; set; }
    public int TotalMessages { get; set; }
    public List<MessageBackupItem> Messages { get; set; } = [];
}

public sealed class MessageImportResult
{
    public int TotalInBackup { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Errors { get; set; } = [];
    public bool IsSuccess => Errors.Count == 0;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MessageBackupDocument))]
[JsonSerializable(typeof(MessageBackupItem))]
[JsonSerializable(typeof(List<MessageBackupItem>))]
[JsonSerializable(typeof(List<string>))]
internal partial class ExportJsonContext : JsonSerializerContext
{
}
