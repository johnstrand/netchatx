using Stanza.Core.Client;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Sharing;

public sealed class Xep0066OutOfBandData : XepFeatureBase
{
    public const string NsOob = "jabber:x:oob";

    public override string Name => "XEP-0066: Out of Band Data";
    public override string FeatureUri => NsOob;

    public static void AttachOobUrl(XmppElement messageElement, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (messageElement.Element("x", NsOob) is null)
        {
            var oob = new XmppElement("x", NsOob);
            oob.Child("url", text: url);
            messageElement.Child(oob);
        }
    }

    public static string? ExtractOobUrl(XmppElement messageElement)
    {
        var oob = messageElement.Element("x", NsOob);
        return oob?.Element("url")?.Value;
    }
}
