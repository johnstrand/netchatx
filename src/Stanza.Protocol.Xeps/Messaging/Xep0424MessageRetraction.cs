using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

public sealed class Xep0424MessageRetraction : XepFeatureBase
{
    public const string NsRetraction1 = "urn:xmpp:message-retract:1";
    public const string NsRetraction0 = "urn:xmpp:message-retract:0";
    public const string NsFallback = "urn:xmpp:fallback:0";
    public const string FallbackMessageText = "/me retracted a previous message, but it's unsupported by your client.";

    public override string Name => "XEP-0424: Message Retraction";
    public override string FeatureUri => NsRetraction1;

    public event Action<string, Jid?>? MessageRetracted; // TargetMessageId, SenderJid

    public static MessageStanza CreateRetractionStanza(Jid to, string targetMessageId, string type = MessageStanza.TypeChat)
    {
        var msg = new MessageStanza(to: to, type: type);
        msg.RawElement.Attr("xmlns", "jabber:client");
        msg.RawElement.Child(new XmppElement("origin-id", "urn:xmpp:sid:0").Attr("id", msg.Id));
        msg.RawElement.Child(new XmppElement("retract", NsRetraction1).Attr("id", targetMessageId));
        msg.RawElement.Child(new XmppElement("fallback", NsFallback).Attr("for", NsRetraction1));
        msg.Body = FallbackMessageText;
        return msg;
    }

    public async Task SendRetractionAsync(Jid to, string targetMessageId, string type = MessageStanza.TypeChat, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var msg = CreateRetractionStanza(to, targetMessageId, type);
        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var retractElem = element.Element("retract", NsRetraction1) ?? element.Element("retract", NsRetraction0);
            if (retractElem is not null)
            {
                var targetId = retractElem.GetAttr("id");
                if (!string.IsNullOrEmpty(targetId))
                {
                    var fromStr = element.GetAttr("from");
                    Jid.TryParse(fromStr, out var fromJid);
                    MessageRetracted?.Invoke(targetId, fromJid);
                    return ValueTask.FromResult(false);
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
