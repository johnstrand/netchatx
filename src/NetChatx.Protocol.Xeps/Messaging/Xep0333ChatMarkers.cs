using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public enum ChatMarkerType
{
    Received,
    Displayed,
    Acknowledged
}

public sealed class Xep0333ChatMarkers : XepFeatureBase
{
    public const string NsChatMarkers = "urn:xmpp:chat-markers:0";
    public const string NsChatMarkersLegacy = "urn:xmpp:chat-markers";

    public override string Name => "XEP-0333: Chat Markers";
    public override string FeatureUri => NsChatMarkers;

    public bool AutoMarkable { get; set; } = true;

    public event Action<string, Jid?, ChatMarkerType>? MarkerReceived;

    public async Task SendDisplayedMarkerAsync(Jid to, string messageId, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (string.IsNullOrEmpty(messageId)) return;

        var msg = new MessageStanza(to: to, type: MessageStanza.TypeChat);
        msg.RawElement.Child(new XmppElement("displayed", NsChatMarkers).Attr("id", messageId));

        await Client.SendStanzaAsync(msg, ct);
    }

    public async Task SendReceivedMarkerAsync(Jid to, string messageId, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (string.IsNullOrEmpty(messageId)) return;

        var msg = new MessageStanza(to: to, type: MessageStanza.TypeChat);
        msg.RawElement.Child(new XmppElement("received", NsChatMarkers).Attr("id", messageId));

        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            string? fromStr = element.GetAttr("from");
            Jid.TryParse(fromStr, out var fromJid);

            var displayed = element.Element("displayed", NsChatMarkers) ?? element.Element("displayed", NsChatMarkersLegacy);
            if (displayed is not null)
            {
                string? id = displayed.GetAttr("id");
                if (!string.IsNullOrEmpty(id))
                {
                    MarkerReceived?.Invoke(id, fromJid, ChatMarkerType.Displayed);
                }
            }

            var received = element.Element("received", NsChatMarkers) ?? element.Element("received", NsChatMarkersLegacy);
            if (received is not null)
            {
                string? id = received.GetAttr("id");
                if (!string.IsNullOrEmpty(id))
                {
                    MarkerReceived?.Invoke(id, fromJid, ChatMarkerType.Received);
                }
            }

            var acknowledged = element.Element("acknowledged", NsChatMarkers) ?? element.Element("acknowledged", NsChatMarkersLegacy);
            if (acknowledged is not null)
            {
                string? id = acknowledged.GetAttr("id");
                if (!string.IsNullOrEmpty(id))
                {
                    MarkerReceived?.Invoke(id, fromJid, ChatMarkerType.Acknowledged);
                }
            }
        }

        return ValueTask.FromResult(true);
    }

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (AutoMarkable && element.Name == "message" && element.GetAttr("type") == "chat")
        {
            // Attach <markable/> if message has body or encrypted content and no marker yet
            if (element.Element("markable", NsChatMarkers) is null &&
                element.Element("markable", NsChatMarkersLegacy) is null &&
                (element.Element("body") is not null || element.Element("encrypted") is not null))
            {
                element.Child(new XmppElement("markable", NsChatMarkers));
            }
        }

        return ValueTask.FromResult(true);
    }
}
