using System.Collections.Concurrent;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Sharing;

public sealed class Xep0234JingleFileTransfer : XepFeatureBase
{
    public const string NsJingle = "urn:xmpp:jingle:1";
    public const string NsFileTransfer = "urn:xmpp:jingle:apps:file-transfer:5";
    public const string NsTransportIbb = "urn:xmpp:jingle:transports:ibb:1";
    public const string NsIbb = "http://jabber.org/protocol/ibb";

    private readonly ConcurrentDictionary<string, JingleFileTransferSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "XEP-0234: Jingle File Transfer";
    public override string FeatureUri => NsFileTransfer;

    public IReadOnlyCollection<JingleFileTransferSession> ActiveSessions => _sessions.Values.ToArray();

    public JingleFileTransferSession? GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;

    // Legacy events for backward compatibility
    public event Action<JingleFileTransferSessionEventArgs>? OfferReceived;
    public event Action<JingleFileTransferSessionEventArgs>? OfferAccepted;
    public event Action<JingleFileTransferSessionEventArgs>? OfferRejected;

    // Enhanced session lifecycle events
    public event Action<JingleFileTransferSession>? TransferOfferReceived;
    public event Action<JingleFileTransferSession>? TransferStarted;
    public event Action<JingleFileTransferProgressEventArgs>? TransferProgress;
    public event Action<JingleFileTransferSession, byte[]>? TransferCompleted;
    public event Action<JingleFileTransferSession, string>? TransferFailed;
    public event Action<JingleFileTransferSession, string>? TransferTerminated;

    public async Task<JingleFileTransferSession> SendFileOfferAsync(
        Jid to,
        string fileName,
        byte[] data,
        string mediaType = "application/octet-stream",
        int blockSize = 4096,
        CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var sessionId = Guid.NewGuid().ToString("N");
        var session = new JingleFileTransferSession
        {
            SessionId = sessionId,
            Peer = to,
            FileName = fileName,
            FileSize = data.LongLength,
            MediaType = mediaType,
            Direction = FileTransferDirection.Outgoing,
            State = FileTransferState.Pending,
            BlockSize = blockSize,
            Data = data
        };

        _sessions[sessionId] = session;

        var iq = IqStanza.CreateSet(to);
        if (Client.BoundJid is not null) iq.From = Client.BoundJid;
        var content = CreateContentElement(session, "initiator");
        iq.RawElement.Child(CreateJingleElement("session-initiate", sessionId, [content]));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            session.State = FileTransferState.Failed;
            session.ErrorMessage = $"Failed to send file offer: {response.ToXmlString()}";
            TransferFailed?.Invoke(session, session.ErrorMessage);
            throw new InvalidOperationException(session.ErrorMessage);
        }

        return session;
    }

    public async Task SendOfferAsync(JingleFileTransferOffer offer, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var session = new JingleFileTransferSession
        {
            SessionId = offer.SessionId,
            Peer = offer.To,
            FileName = offer.Name,
            FileSize = offer.Size,
            MediaType = offer.MediaType,
            Direction = FileTransferDirection.Outgoing,
            State = FileTransferState.Pending,
            BlockSize = 4096
        };

        _sessions[offer.SessionId] = session;

        var iq = IqStanza.CreateSet(offer.To);
        if (Client.BoundJid is not null) iq.From = Client.BoundJid;
        var content = CreateContentElement(session, "initiator");
        iq.RawElement.Child(CreateJingleElement("session-initiate", offer.SessionId, [content]));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            session.State = FileTransferState.Failed;
            session.ErrorMessage = $"Failed to send file offer: {response.ToXmlString()}";
            TransferFailed?.Invoke(session, session.ErrorMessage);
            throw new InvalidOperationException(session.ErrorMessage);
        }
    }

    public async Task AcceptOfferAsync(string sessionId, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"No Jingle file transfer session found with ID: {sessionId}");
        }

        session.ReceiveBuffer ??= new MemoryStream();
        session.State = FileTransferState.Accepted;

        var iq = IqStanza.CreateSet(session.Peer);
        if (Client.BoundJid is not null) iq.From = Client.BoundJid;
        var content = CreateContentElement(session, "initiator");
        iq.RawElement.Child(CreateJingleElement("session-accept", sessionId, [content]));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            session.State = FileTransferState.Failed;
            session.ErrorMessage = $"Failed to accept file offer: {response.ToXmlString()}";
            TransferFailed?.Invoke(session, session.ErrorMessage);
            throw new InvalidOperationException(session.ErrorMessage);
        }
    }

    public Task SendAcceptAsync(Jid to, string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            _sessions[sessionId] = new JingleFileTransferSession
            {
                SessionId = sessionId,
                Peer = to,
                Direction = FileTransferDirection.Incoming,
                State = FileTransferState.Accepted
            };
        }
        return AcceptOfferAsync(sessionId, ct);
    }

    public async Task RejectOfferAsync(string sessionId, string reason = "decline", CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"No Jingle file transfer session found with ID: {sessionId}");
        }

        session.Cts.Cancel();
        session.State = reason == "cancel" ? FileTransferState.Cancelled : FileTransferState.Rejected;

        var reasonElement = new XmppElement("reason")
            .Child(new XmppElement(reason));

        var iq = IqStanza.CreateSet(session.Peer);
        if (Client.BoundJid is not null) iq.From = Client.BoundJid;
        iq.RawElement.Child(CreateJingleElement("session-terminate", sessionId, [reasonElement]));

        TransferTerminated?.Invoke(session, reason);

        try
        {
            await Client.SendIqAsync(iq, TimeSpan.FromSeconds(2), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException)
        {
            // Ignore transport close or timeout during teardown
        }
    }

    public Task SendRejectAsync(Jid to, string sessionId, string reason = "decline", CancellationToken ct = default)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            _sessions[sessionId] = new JingleFileTransferSession
            {
                SessionId = sessionId,
                Peer = to,
                Direction = FileTransferDirection.Incoming,
                State = FileTransferState.Rejected
            };
        }
        return RejectOfferAsync(sessionId, reason, ct);
    }

    public Task CancelTransferAsync(string sessionId, CancellationToken ct = default)
    {
        return RejectOfferAsync(sessionId, "cancel", ct);
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name != "iq")
        {
            return true;
        }

        var iq = new IqStanza(element);

        // Only handle IQ 'set' or 'get' requests. Allow result/error responses to pass through to pending IQ listeners.
        if (!iq.IsSet && !iq.IsGet)
        {
            return true;
        }

        var jingle = element.Element("jingle", NsJingle);
        if (jingle is not null)
        {
            await HandleJingleIqAsync(client, iq, jingle, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (element.Element("open", NsIbb) is not null ||
            element.Element("data", NsIbb) is not null ||
            element.Element("close", NsIbb) is not null)
        {
            await HandleIbbIqAsync(client, iq, cancellationToken).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private async Task HandleJingleIqAsync(XmppClient client, IqStanza iq, XmppElement jingle, CancellationToken ct)
    {
        var action = jingle.GetAttr("action") ?? string.Empty;
        var sid = jingle.GetAttr("sid") ?? string.Empty;
        var from = iq.From ?? Jid.Parse("unknown@example.invalid");

        // Acknowledge IQ set immediately per Jingle specification
        if (iq.IsSet)
        {
            var ack = iq.CreateResult();
            await client.SendStanzaAsync(ack, ct).ConfigureAwait(false);
        }

        switch (action)
        {
            case "session-initiate":
                {
                    string? name = null;
                    long? size = null;
                    string? mediaType = null;
                    var blockSize = 4096;

                    var description = jingle.Element("description", NsFileTransfer)
                        ?? jingle.Element("content")?.Element("description", NsFileTransfer);
                    var file = description?.Element("file");
                    if (file is not null)
                    {
                        name = file.Element("name")?.Value;
                        if (long.TryParse(file.Element("size")?.Value, out var parsedSize))
                        {
                            size = parsedSize;
                        }
                        mediaType = file.Element("media-type")?.Value;
                    }

                    var transport = jingle.Element("transport", NsTransportIbb)
                        ?? jingle.Element("content")?.Element("transport", NsTransportIbb);
                    if (transport is not null && int.TryParse(transport.GetAttr("block-size"), out var parsedBlockSize) && parsedBlockSize > 0)
                    {
                        blockSize = parsedBlockSize;
                    }

                    var session = new JingleFileTransferSession
                    {
                        SessionId = sid,
                        Peer = from,
                        FileName = name ?? "file.bin",
                        FileSize = size ?? 0,
                        MediaType = mediaType ?? "application/octet-stream",
                        Direction = FileTransferDirection.Incoming,
                        State = FileTransferState.Pending,
                        BlockSize = blockSize
                    };

                    _sessions[sid] = session;

                    var args = new JingleFileTransferSessionEventArgs(sid, from, name, size, mediaType);
                    OfferReceived?.Invoke(args);
                    TransferOfferReceived?.Invoke(session);
                    break;
                }

            case "session-accept":
                {
                    if (_sessions.TryGetValue(sid, out var session))
                    {
                        session.State = FileTransferState.Accepted;
                        session.AcceptedTcs.TrySetResult(true);

                        var args = new JingleFileTransferSessionEventArgs(sid, from, session.FileName, session.FileSize, session.MediaType);
                        OfferAccepted?.Invoke(args);

                        if (session.Direction == FileTransferDirection.Outgoing && session.Data is not null)
                        {
                            _ = Task.Run(() => ExecuteOutboundTransferAsync(client, session), CancellationToken.None);
                        }
                    }
                    else
                    {
                        var args = new JingleFileTransferSessionEventArgs(sid, from, null, null, null);
                        OfferAccepted?.Invoke(args);
                    }
                    break;
                }

            case "session-terminate":
                {
                    var reason = jingle.Element("reason")?.Elements().FirstOrDefault()?.Name ?? "terminate";

                    if (_sessions.TryGetValue(sid, out var session))
                    {
                        if (reason == "success")
                        {
                            session.State = FileTransferState.Completed;
                            var receivedBytes = session.ReceiveBuffer?.ToArray() ?? session.Data ?? [];
                            session.Data = receivedBytes;
                            session.CompletedTcs.TrySetResult(receivedBytes);
                            TransferCompleted?.Invoke(session, receivedBytes);
                        }
                        else
                        {
                            session.Cts.Cancel();
                            session.State = reason == "decline"
                                ? FileTransferState.Rejected
                                : (reason == "cancel" ? FileTransferState.Cancelled : FileTransferState.Failed);
                            session.ErrorMessage = $"Transfer terminated by peer: {reason}";
                            session.AcceptedTcs.TrySetResult(false);
                            session.CompletedTcs.TrySetException(new InvalidOperationException(session.ErrorMessage));

                            var args = new JingleFileTransferSessionEventArgs(sid, from, session.FileName, session.FileSize, session.MediaType);
                            OfferRejected?.Invoke(args);
                            TransferTerminated?.Invoke(session, reason);
                        }
                    }
                    else
                    {
                        var args = new JingleFileTransferSessionEventArgs(sid, from, null, null, null);
                        OfferRejected?.Invoke(args);
                    }
                    break;
                }
        }
    }

    private async Task HandleIbbIqAsync(XmppClient client, IqStanza iq, CancellationToken ct)
    {
        var open = iq.RawElement.Element("open", NsIbb);
        if (open is not null)
        {
            var sid = open.GetAttr("sid") ?? string.Empty;
            if (_sessions.TryGetValue(sid, out var session))
            {
                session.ReceiveBuffer ??= new MemoryStream();
                session.State = FileTransferState.Transferring;
                TransferStarted?.Invoke(session);

                await client.SendStanzaAsync(iq.CreateResult(), ct).ConfigureAwait(false);
            }
            else
            {
                await client.SendStanzaAsync(iq.CreateError("item-not-found"), ct).ConfigureAwait(false);
            }
            return;
        }

        var data = iq.RawElement.Element("data", NsIbb);
        if (data is not null)
        {
            var sid = data.GetAttr("sid") ?? string.Empty;
            if (_sessions.TryGetValue(sid, out var session))
            {
                try
                {
                    var base64 = data.Value ?? string.Empty;
                    var bytes = Convert.FromBase64String(base64);
                    session.ReceiveBuffer?.Write(bytes, 0, bytes.Length);
                    session.BytesTransferred += bytes.Length;

                    await client.SendStanzaAsync(iq.CreateResult(), ct).ConfigureAwait(false);

                    TransferProgress?.Invoke(new JingleFileTransferProgressEventArgs(
                        session.SessionId, session.Peer, session.BytesTransferred, session.FileSize, session.Progress));
                }
                catch (Exception ex)
                {
                    session.State = FileTransferState.Failed;
                    session.ErrorMessage = ex.Message;
                    await client.SendStanzaAsync(iq.CreateError("bad-request"), ct).ConfigureAwait(false);
                }
            }
            else
            {
                await client.SendStanzaAsync(iq.CreateError("item-not-found"), ct).ConfigureAwait(false);
            }
            return;
        }

        var close = iq.RawElement.Element("close", NsIbb);
        if (close is not null)
        {
            var sid = close.GetAttr("sid") ?? string.Empty;
            if (_sessions.TryGetValue(sid, out var session))
            {
                await client.SendStanzaAsync(iq.CreateResult(), ct).ConfigureAwait(false);
            }
            else
            {
                await client.SendStanzaAsync(iq.CreateError("item-not-found"), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteOutboundTransferAsync(XmppClient client, JingleFileTransferSession session)
    {
        try
        {
            session.State = FileTransferState.Transferring;
            TransferStarted?.Invoke(session);

            var data = session.Data ?? [];
            var blockSize = session.BlockSize > 0 ? session.BlockSize : 4096;

            // 1. Open IBB stream
            var openIq = IqStanza.CreateSet(session.Peer);
            if (client.BoundJid is not null) openIq.From = client.BoundJid;
            openIq.RawElement.Child(new XmppElement("open", NsIbb)
                .Attr("block-size", blockSize.ToString())
                .Attr("sid", session.SessionId)
                .Attr("stanza", "iq"));

            var openResp = await client.SendIqAsync(openIq, cancellationToken: session.Cts.Token).ConfigureAwait(false);
            if (openResp.IsError)
            {
                throw new InvalidOperationException($"IBB open rejected: {openResp.ToXmlString()}");
            }

            // 2. Stream data chunks
            var offset = 0;
            var seq = 0;
            while (offset < data.Length)
            {
                session.Cts.Token.ThrowIfCancellationRequested();

                var length = Math.Min(blockSize, data.Length - offset);
                var chunkBase64 = Convert.ToBase64String(data.AsSpan(offset, length));

                var dataIq = IqStanza.CreateSet(session.Peer);
                if (client.BoundJid is not null) dataIq.From = client.BoundJid;
                dataIq.RawElement.Child(new XmppElement("data", NsIbb)
                    .Attr("seq", seq.ToString())
                    .Attr("sid", session.SessionId)
                    .Text(chunkBase64));

                var dataResp = await client.SendIqAsync(dataIq, cancellationToken: session.Cts.Token).ConfigureAwait(false);
                if (dataResp.IsError)
                {
                    throw new InvalidOperationException($"IBB data chunk {seq} rejected: {dataResp.ToXmlString()}");
                }

                offset += length;
                seq = (seq + 1) % 65536;
                session.BytesTransferred = offset;

                TransferProgress?.Invoke(new JingleFileTransferProgressEventArgs(
                    session.SessionId, session.Peer, session.BytesTransferred, session.FileSize, session.Progress));
            }

            // 3. Close IBB stream
            var closeIq = IqStanza.CreateSet(session.Peer);
            if (client.BoundJid is not null) closeIq.From = client.BoundJid;
            closeIq.RawElement.Child(new XmppElement("close", NsIbb).Attr("sid", session.SessionId));
            _ = await client.SendIqAsync(closeIq, cancellationToken: session.Cts.Token).ConfigureAwait(false);

            // 4. Terminate Jingle session with success
            var termIq = IqStanza.CreateSet(session.Peer);
            if (client.BoundJid is not null) termIq.From = client.BoundJid;
            termIq.RawElement.Child(CreateJingleElement("session-terminate", session.SessionId, [
                new XmppElement("reason").Child(new XmppElement("success"))
            ]));
            _ = await client.SendIqAsync(termIq, cancellationToken: session.Cts.Token).ConfigureAwait(false);

            session.State = FileTransferState.Completed;
            session.CompletedTcs.TrySetResult(data);
            TransferCompleted?.Invoke(session, data);
        }
        catch (OperationCanceledException)
        {
            session.State = FileTransferState.Cancelled;
            TransferTerminated?.Invoke(session, "cancel");
        }
        catch (Exception ex)
        {
            session.State = FileTransferState.Failed;
            session.ErrorMessage = ex.Message;
            session.CompletedTcs.TrySetException(ex);
            TransferFailed?.Invoke(session, ex.Message);

            try
            {
                var termIq = IqStanza.CreateSet(session.Peer);
                termIq.RawElement.Child(CreateJingleElement("session-terminate", session.SessionId, [
                    new XmppElement("reason").Child(new XmppElement("failed-transport"))
                ]));
                await client.SendIqAsync(termIq).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static XmppElement CreateContentElement(JingleFileTransferSession session, string creator)
    {
        return new XmppElement("content")
            .Attr("creator", creator)
            .Attr("name", "file-transfer")
            .Child(new XmppElement("description", NsFileTransfer)
                .Child(new XmppElement("file")
                    .Child(new XmppElement("name") { Value = session.FileName })
                    .Child(new XmppElement("size") { Value = session.FileSize.ToString() })
                    .Child(new XmppElement("media-type") { Value = session.MediaType })))
            .Child(new XmppElement("transport", NsTransportIbb)
                .Attr("block-size", session.BlockSize.ToString())
                .Attr("sid", session.SessionId));
    }

    private static XmppElement CreateJingleElement(string action, string sessionId, IReadOnlyList<XmppElement>? children = null)
    {
        var jingle = new XmppElement("jingle", NsJingle)
            .Attr("action", action)
            .Attr("sid", sessionId)
            .Attr("xmlns", NsJingle);

        if (children is not null)
        {
            foreach (var child in children)
            {
                jingle.Child(child);
            }
        }

        return jingle;
    }
}
