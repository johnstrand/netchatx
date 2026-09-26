using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

public sealed class Xep0184MessageDeliveryReceipts : XepFeatureBase
{
    public const string NsReceipts = "urn:xmpp:receipts";

    public override string Name => "XEP-0184: Message Delivery Receipts";
    public override string FeatureUri => NsReceipts;

    public bool AutoAcknowledge { get; set; } = true;
    public bool AutoRequest { get; set; } = true;

    public event Action<string, Jid?>? ReceiptReceived; // StanzaId, From

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var received = element.Element("received", NsReceipts);
            if (received is not null)
            {
                var id = received.GetAttr("id");
                if (!string.IsNullOrEmpty(id))
                {
                    var fromStr = element.GetAttr("from");
                    Jid.TryParse(fromStr, out var fromJid);
                    ReceiptReceived?.Invoke(id, fromJid);
                }
            }

            var request = element.Element("request", NsReceipts);
            if (request is not null && AutoAcknowledge)
            {
                var id = element.GetAttr("id");
                var fromStr = element.GetAttr("from");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
                {
                    var ack = new MessageStanza(to: fromJid, type: MessageStanza.TypeNormal);
                    ack.RawElement.Child(new XmppElement("received", NsReceipts).Attr("id", id));
                    await client.SendStanzaAsync(ack, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return true;
    }

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (AutoRequest && element.Name == "message" && element.GetAttr("type") == "chat")
        {
            if (element.Element("request", NsReceipts) is null)
            {
                element.Child(new XmppElement("request", NsReceipts));
            }
        }

        return ValueTask.FromResult(true);
    }
}
