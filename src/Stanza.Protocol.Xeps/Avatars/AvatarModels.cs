using Stanza.Core;

namespace Stanza.Protocol.Xeps.Avatars;

public sealed record AvatarMetadata(
    string Id,
    string MimeType,
    long Bytes,
    int? Width = null,
    int? Height = null,
    string? Url = null);

public sealed class AvatarChangedEventArgs : EventArgs
{
    public required Jid Jid { get; init; }
    public string? Hash { get; init; }
    public byte[]? Data { get; init; }
    public string? MimeType { get; init; }
    public bool IsCleared { get; init; }
    public string Source { get; init; } = "unknown";
}
