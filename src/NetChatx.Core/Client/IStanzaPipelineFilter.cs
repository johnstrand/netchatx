using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;

namespace NetChatx.Core.Client;

public interface IIncomingStanzaFilter
{
    /// <summary>
    /// Invoked when a new top-level XML element/stanza arrives.
    /// Return false to stop further processing/dispatch (e.g. handled internally by filter).
    /// </summary>
    ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default);
}

public interface IOutgoingStanzaFilter
{
    /// <summary>
    /// Invoked immediately prior to an outgoing stanza being serialized and written to the wire.
    /// Allows modifying, decorating, or canceling the transmission.
    /// </summary>
    ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default);
}
