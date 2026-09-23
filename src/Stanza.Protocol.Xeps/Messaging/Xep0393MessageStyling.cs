using Stanza.Core.Client;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

/// <summary>
/// Implements XEP-0393: Message Styling (urn:xmpp:styling:0).
/// Defines lightweight formatted text syntax for instant messages and allows signaling unstyled messages.
/// </summary>
public sealed class Xep0393MessageStyling : XepFeatureBase
{
    public const string NsStyling = "urn:xmpp:styling:0";

    public override string Name => "XEP-0393: Message Styling";
    public override string FeatureUri => NsStyling;

    /// <summary>
    /// Checks if a message element contains the &lt;unstyled xmlns='urn:xmpp:styling:0'/&gt; flag.
    /// </summary>
    public static bool IsUnstyled(XmppElement element)
        => element.Element("unstyled", NsStyling) is not null;

    /// <summary>
    /// Attaches &lt;unstyled xmlns='urn:xmpp:styling:0'/&gt; to disable styling for this message.
    /// </summary>
    public static void AttachUnstyled(XmppElement element)
        => element.Child(new XmppElement("unstyled", NsStyling));

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(true);
    }
}
