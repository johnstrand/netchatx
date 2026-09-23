using Stanza.Core.Client;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

public sealed class Xep0359StanzaIds : XepFeatureBase
{
    public const string NsSid = "urn:xmpp:sid:0";

    public override string Name => "XEP-0359: Unique and Stable Stanza IDs";
    public override string FeatureUri => NsSid;

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var id = element.GetAttr("id") ?? Guid.NewGuid().ToString();

            // 1. Attach <stanza-id by="..." id="..." xmlns="urn:xmpp:sid:0" /> per XEP-0359 §4
            if (element.Element("stanza-id", NsSid) is null && client.BoundJid is not null)
            {
                var byJid = client.BoundJid.BareJid.ToString();
                if (!string.IsNullOrEmpty(byJid))
                {
                    element.Child(new XmppElement("stanza-id", NsSid)
                        .Attr("by", byJid)
                        .Attr("id", id));
                }
            }

            // 2. Attach <origin-id id="..." xmlns="urn:xmpp:sid:0" /> per XEP-0359 §3
            if (element.Element("origin-id", NsSid) is null)
            {
                element.Child(new XmppElement("origin-id", NsSid).Attr("id", id));
            }
        }

        return ValueTask.FromResult(true);
    }
}
