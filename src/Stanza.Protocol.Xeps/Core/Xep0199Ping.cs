using System.Diagnostics;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Core;

public sealed class Xep0199Ping : XepFeatureBase
{
    public const string NsPing = "urn:xmpp:ping";

    public override string Name => "XEP-0199: XMPP Ping";
    public override string FeatureUri => NsPing;

    public async Task<TimeSpan> PingAsync(Jid? target = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateGet(target);
        iq.RawElement.Child(new XmppElement("ping", NsPing));

        var sw = Stopwatch.StartNew();
        var result = await Client.SendIqAsync(iq, timeout, ct);
        sw.Stop();

        if (result.IsError)
        {
            throw new InvalidOperationException($"Ping returned error: {result.ToXmlString()}");
        }

        return sw.Elapsed;
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "iq" && element.GetAttr("type") == "get")
        {
            var ping = element.Element("ping", NsPing);
            if (ping is not null)
            {
                var iq = new IqStanza(element);
                var pong = iq.CreateResult();
                await client.SendStanzaAsync(pong, cancellationToken);
                return false; // Handled
            }
        }

        return true;
    }
}
