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

    public override string Name => "XEP-0234: Jingle File Transfer";
    public override string FeatureUri => NsFileTransfer;

    public event Action<JingleFileTransferSessionEventArgs>? OfferReceived;
    public event Action<JingleFileTransferSessionEventArgs>? OfferAccepted;
    public event Action<JingleFileTransferSessionEventArgs>? OfferRejected;

    public async Task SendOfferAsync(JingleFileTransferOffer offer, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet(offer.To.BareJid);
        iq.RawElement.Child(CreateJingleElement("session-initiate", offer.SessionId, [CreateFileDescriptionElement(offer)]));
        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to send file offer: {response.ToXmlString()}");
        }
    }

    public async Task SendAcceptAsync(Jid to, string sessionId, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet(to.BareJid);
        iq.RawElement.Child(CreateJingleElement("session-accept", sessionId));
        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to accept file offer: {response.ToXmlString()}");
        }
    }

    public async Task SendRejectAsync(Jid to, string sessionId, string reason = "decline", CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var reasonElement = new XmppElement("reason")
            .Child(new XmppElement(reason));

        var iq = IqStanza.CreateSet(to.BareJid);
        iq.RawElement.Child(CreateJingleElement("session-terminate", sessionId, [reasonElement]));
        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to reject file offer: {response.ToXmlString()}");
        }
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name != "iq")
        {
            return ValueTask.FromResult(true);
        }

        var jingle = element.Element("jingle", NsJingle);
        if (jingle is null)
        {
            return ValueTask.FromResult(true);
        }

        var action = jingle.GetAttr("action") ?? string.Empty;
        var sid = jingle.GetAttr("sid") ?? string.Empty;
        var from = Jid.TryParse(element.GetAttr("from"), out var parsedFrom)
            ? parsedFrom
            : Jid.Parse("unknown@example.invalid");

        string? name = null;
        long? size = null;
        string? mediaType = null;

        var description = jingle.Element("description", NsFileTransfer);
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

        var args = new JingleFileTransferSessionEventArgs(sid, from, name, size, mediaType);
        switch (action)
        {
            case "session-initiate":
                OfferReceived?.Invoke(args);
                break;
            case "session-accept":
                OfferAccepted?.Invoke(args);
                break;
            case "session-terminate":
                OfferRejected?.Invoke(args);
                break;
        }

        return ValueTask.FromResult(true);
    }

    private static XmppElement CreateFileDescriptionElement(JingleFileTransferOffer offer)
    {
        return new XmppElement("description", NsFileTransfer)
            .Child(new XmppElement("file")
                .Child(new XmppElement("name") { Value = offer.Name })
                .Child(new XmppElement("size") { Value = offer.Size.ToString() })
                .Child(new XmppElement("media-type") { Value = offer.MediaType }));
    }

    private static XmppElement CreateJingleElement(string action, string sessionId, IReadOnlyList<XmppElement>? children = null)
    {
        var jingle = new XmppElement("jingle", NsJingle)
            .Attr("action", action)
            .Attr("sid", sessionId)
            .Attr("initiator", "")
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
