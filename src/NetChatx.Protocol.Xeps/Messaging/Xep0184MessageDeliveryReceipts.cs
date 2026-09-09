using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

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
                string? id = received.GetAttr("id");
                if (!string.IsNullOrEmpty(id))
                {
                    string? fromStr = element.GetAttr("from");
                    Jid.TryParse(fromStr, out var fromJid);
                    ReceiptReceived?.Invoke(id, fromJid);
                }
            }

            var request = element.Element("request", NsReceipts);
            if (request is not null && AutoAcknowledge)
            {
                string? id = element.GetAttr("id");
                string? fromStr = element.GetAttr("from");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
                {
                    var ack = new MessageStanza(to: fromJid, type: MessageStanza.TypeNormal);
                    ack.RawElement.Child(new XmppElement("received", NsReceipts).Attr("id", id));
                    await client.SendStanzaAsync(ack, cancellationToken);
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
