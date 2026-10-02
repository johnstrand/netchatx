using Stanza.Core.Client;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Resilience;

public sealed class Xep0352ClientStateIndication : XepFeatureBase
{
    public const string NsCsi = "urn:xmpp:csi:0";

    public override string Name => "XEP-0352: Client State Indication";
    public override string FeatureUri => NsCsi;

    public bool IsActive { get; private set; } = true;

    public async Task SetActiveAsync(bool active, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        IsActive = active;

        var elem = new XmppElement(active ? "active" : "inactive", NsCsi);
        await Client.SendElementAsync(elem, ct).ConfigureAwait(false);
    }
}
