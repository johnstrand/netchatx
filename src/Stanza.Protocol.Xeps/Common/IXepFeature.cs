using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;

namespace Stanza.Protocol.Xeps.Common;

public interface IXepFeature
{
    string Name { get; }
    string FeatureUri { get; }

    ValueTask AttachAsync(XmppClient client, CancellationToken cancellationToken = default);
    ValueTask DetachAsync(XmppClient client, CancellationToken cancellationToken = default);
}

public abstract class XepFeatureBase : IXepFeature, IIncomingStanzaFilter, IOutgoingStanzaFilter
{
    protected XmppClient? Client { get; private set; }

    public abstract string Name { get; }
    public abstract string FeatureUri { get; }

    public virtual ValueTask AttachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        Client = client;
        client.AddIncomingFilter(this);
        client.AddOutgoingFilter(this);
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask DetachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        Client = null;
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(true);
    }

    public virtual ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(true);
    }
}
