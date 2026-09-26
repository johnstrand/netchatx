using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Core;

public sealed class Xep0030ServiceDiscovery : XepFeatureBase
{
    public const string NsInfo = "http://jabber.org/protocol/disco#info";
    public const string NsItems = "http://jabber.org/protocol/disco#items";

    public override string Name => "XEP-0030: Service Discovery";
    public override string FeatureUri => NsInfo;

    private readonly HashSet<string> _supportedFeatures = new(StringComparer.Ordinal)
    {
        NsInfo,
        NsItems,
        "urn:xmpp:ping",
        "urn:xmpp:styling:0"
    };

    public IReadOnlySet<string> SupportedFeatures => _supportedFeatures;

    public void RegisterFeature(string featureUri) => _supportedFeatures.Add(featureUri);

    public async Task<DiscoInfoResult> DiscoverInfoAsync(Jid? target = null, string? node = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateGet(target);
        var query = new XmppElement("query", NsInfo);
        if (!string.IsNullOrEmpty(node)) query.Attr("node", node);
        iq.RawElement.Child(query);

        var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        var resQuery = resultIq.RawElement.Element("query", NsInfo);
        if (resQuery is null) return new DiscoInfoResult([], new HashSet<string>());

        var identities = resQuery.Elements("identity").Select(e => new DiscoIdentity(
            e.GetAttr("category") ?? "",
            e.GetAttr("type") ?? "",
            e.GetAttr("name"))).ToList();

        var features = resQuery.Elements("feature").Select(e => e.GetAttr("var") ?? "").Where(f => !string.IsNullOrEmpty(f)).ToHashSet();

        return new DiscoInfoResult(identities, features);
    }

    public async Task<List<DiscoItem>> DiscoverItemsAsync(Jid? target = null, string? node = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateGet(target);
        var query = new XmppElement("query", NsItems);
        if (!string.IsNullOrEmpty(node)) query.Attr("node", node);
        iq.RawElement.Child(query);

        var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        var resQuery = resultIq.RawElement.Element("query", NsItems);
        if (resQuery is null) return [];

        return resQuery.Elements("item").Select(e => new DiscoItem(
            Jid.Parse(e.GetAttr("jid") ?? ""),
            e.GetAttr("name"),
            e.GetAttr("node"))).ToList();
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "iq" && element.GetAttr("type") == "get")
        {
            var query = element.Element("query", NsInfo);
            if (query is not null)
            {
                var iq = new IqStanza(element);
                var respQuery = new XmppElement("query", NsInfo)
                    .Child(new XmppElement("identity")
                        .Attr("category", "client")
                        .Attr("type", "pc")
                        .Attr("name", "Stanza"));

                foreach (var feat in _supportedFeatures)
                {
                    respQuery.Child(new XmppElement("feature").Attr("var", feat));
                }

                var responseIq = iq.CreateResult(respQuery);
                await client.SendStanzaAsync(responseIq, cancellationToken).ConfigureAwait(false);
                return false; // Handled internally
            }
        }

        return true;
    }
}

public record DiscoIdentity(string Category, string Type, string? Name);
public record DiscoItem(Jid Jid, string? Name, string? Node);
public record DiscoInfoResult(IReadOnlyList<DiscoIdentity> Identities, IReadOnlySet<string> Features);
