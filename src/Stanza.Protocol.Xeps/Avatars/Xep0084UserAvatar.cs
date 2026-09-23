using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Avatars;

public sealed class Xep0084UserAvatar : XepFeatureBase
{
    public const string NsMetadata = "urn:xmpp:avatar:metadata";
    public const string NsData = "urn:xmpp:avatar:data";
    public const string NsPubSub = "http://jabber.org/protocol/pubsub";
    public const string NsPubSubEvent = "http://jabber.org/protocol/pubsub#event";

    public override string Name => "XEP-0084: User Avatar";
    public override string FeatureUri => NsMetadata;

    public event Action<Jid, AvatarMetadata>? AvatarMetadataReceived;
    public event Action<Jid>? AvatarMetadataCleared;

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name != "message")
        {
            return ValueTask.FromResult(true);
        }

        var pubsubEvent = element.Element("event", NsPubSubEvent);
        if (pubsubEvent is null)
        {
            return ValueTask.FromResult(true);
        }

        var items = pubsubEvent.Element("items");
        if (items is null || items.GetAttr("node") != NsMetadata)
        {
            return ValueTask.FromResult(true);
        }

        var fromStr = element.GetAttr("from");
        if (string.IsNullOrEmpty(fromStr) || !Jid.TryParse(fromStr, out var fromJid))
        {
            return ValueTask.FromResult(true);
        }

        var item = items.Element("item");
        if (item is null)
        {
            return ValueTask.FromResult(true);
        }

        var metadata = item.Element("metadata", NsMetadata);
        if (metadata is null)
        {
            return ValueTask.FromResult(true);
        }

        var info = metadata.Element("info");
        if (info is null)
        {
            // Empty metadata signifies avatar removal per XEP-0084 §4
            AvatarMetadataCleared?.Invoke(fromJid.BareJid);
            return ValueTask.FromResult(true);
        }

        var id = info.GetAttr("id");
        var mime = info.GetAttr("type") ?? "image/png";
        var bytesStr = info.GetAttr("bytes");
        long.TryParse(bytesStr, out var bytes);

        int? width = null;
        if (int.TryParse(info.GetAttr("width"), out var w)) width = w;

        int? height = null;
        if (int.TryParse(info.GetAttr("height"), out var h)) height = h;

        var url = info.GetAttr("url");

        if (!string.IsNullOrEmpty(id))
        {
            var meta = new AvatarMetadata(id, mime, bytes, width, height, url);
            AvatarMetadataReceived?.Invoke(fromJid.BareJid, meta);
        }

        return ValueTask.FromResult(true);
    }

    public async Task<byte[]?> FetchAvatarDataAsync(Jid targetJid, string hash, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var iq = IqStanza.CreateGet(targetJid.BareJid);
        var pubsub = new XmppElement("pubsub", NsPubSub)
            .Child(new XmppElement("items")
                .Attr("node", NsData)
                .Child(new XmppElement("item").Attr("id", hash)));
        iq.RawElement.Child(pubsub);

        try
        {
            var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
            if (resultIq.IsError) return null;

            var dataElem = resultIq.RawElement
                .Element("pubsub", NsPubSub)?
                .Element("items")?
                .Element("item")?
                .Element("data", NsData);

            if (dataElem is not null && !string.IsNullOrWhiteSpace(dataElem.Value))
            {
                return Convert.FromBase64String(dataElem.Value.Trim());
            }
        }
        catch
        {
            // Soft failure fetching avatar data from PEP
        }

        return null;
    }

    public async Task PublishAvatarDataAsync(string hash, byte[] data, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(data);

        var iq = IqStanza.CreateSet();
        var pubsub = new XmppElement("pubsub", NsPubSub)
            .Child(new XmppElement("publish")
                .Attr("node", NsData)
                .Child(new XmppElement("item")
                    .Attr("id", hash)
                    .Child(new XmppElement("data", NsData) { Value = Convert.ToBase64String(data) })));
        iq.RawElement.Child(pubsub);

        await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task PublishAvatarMetadataAsync(string hash, string mimeType, long bytes, int? width = null, int? height = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var iq = IqStanza.CreateSet();
        var info = new XmppElement("info")
            .Attr("id", hash)
            .Attr("type", mimeType)
            .Attr("bytes", bytes.ToString());

        if (width.HasValue) info.Attr("width", width.Value.ToString());
        if (height.HasValue) info.Attr("height", height.Value.ToString());

        var metadata = new XmppElement("metadata", NsMetadata).Child(info);
        var pubsub = new XmppElement("pubsub", NsPubSub)
            .Child(new XmppElement("publish")
                .Attr("node", NsMetadata)
                .Child(new XmppElement("item")
                    .Attr("id", hash)
                    .Child(metadata)));
        iq.RawElement.Child(pubsub);

        await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task ClearAvatarAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet();
        var metadata = new XmppElement("metadata", NsMetadata);
        var pubsub = new XmppElement("pubsub", NsPubSub)
            .Child(new XmppElement("publish")
                .Attr("node", NsMetadata)
                .Child(new XmppElement("item").Child(metadata)));
        iq.RawElement.Child(pubsub);

        await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
    }
}
