using Stanza.Core;

namespace Stanza.Protocol.Xeps.Sharing;

public sealed record JingleFileTransferOffer(
    string SessionId,
    string Name,
    long Size,
    string MediaType,
    Jid To,
    DateTimeOffset CreatedAtUtc);

public sealed record JingleFileTransferSessionEventArgs(
    string SessionId,
    Jid Peer,
    string? Name,
    long? Size,
    string? MediaType);
