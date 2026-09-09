using NetChatx.Core.Client;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public sealed class Xep0359StanzaIds : XepFeatureBase
{
    public const string NsSid = "urn:xmpp:sid:0";

    public override string Name => "XEP-0359: Unique and Stable Stanza IDs";
    public override string FeatureUri => NsSid;

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message" && element.Element("origin-id", NsSid) is null)
        {
            element.Child(new XmppElement("origin-id", NsSid).Attr("id", Guid.NewGuid().ToString("N")));
        }

        return ValueTask.FromResult(true);
    }
}
