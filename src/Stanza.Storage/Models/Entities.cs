namespace Stanza.Storage.Models;

public enum MessageDirection
{
    Inbound = 0,
    Outbound = 1
}

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string AccountJid { get; set; }
    public required string RemoteJid { get; set; }
    public required string SenderJid { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public MessageDirection Direction { get; set; }
    public required string Body { get; set; }
    public string? StanzaId { get; set; }
    public string? OriginId { get; set; }
    public string? ReplaceId { get; set; }
    public bool IsEncrypted { get; set; }
    public string? EncryptionType { get; set; }
    public bool IsRead { get; set; }
    public string? RawXml { get; set; }
}

public sealed class ChatReadMarker
{
    public required string AccountJid { get; set; }
    public required string RemoteJid { get; set; }
    public required string ParticipantJid { get; set; }
    public required string LastReadMessageId { get; set; }
    public DateTimeOffset LastReadTimestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MessageReaction
{
    public required string AccountJid { get; set; }
    public required string RemoteJid { get; set; }
    public required string MessageId { get; set; }
    public required string SenderJid { get; set; }
    public required string Emoji { get; set; }
}

public sealed class RosterContact
{
    public required string AccountJid { get; set; }
    public required string ContactJid { get; set; }
    public string? Name { get; set; }
    public string Subscription { get; set; } = "none";
    public string? Groups { get; set; }
    public string? PresenceShow { get; set; }
    public string? PresenceStatus { get; set; }
}

public sealed class AccountProfile
{
    public required string Jid { get; set; }
    public required string Password { get; set; }
    public string Resource { get; set; } = "Stanza";
    public string? Host { get; set; }
    public int Port { get; set; } = 5222;
    public bool UseDirectTls { get; set; } = false;
    public bool AllowUntrustedCertificates { get; set; } = false;
    public bool IsActive { get; set; } = true;
}

public sealed class AvatarRecord
{
    public required string Jid { get; set; }
    public required string Hash { get; set; }
    public required string MimeType { get; set; }
    public required byte[] Data { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
