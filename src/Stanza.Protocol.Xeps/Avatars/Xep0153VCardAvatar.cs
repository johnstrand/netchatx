using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Avatars;

public sealed class Xep0153VCardAvatar : XepFeatureBase
{
    public const string NsVCard = "vcard-temp";
    public const string NsVCardUpdate = "vcard-temp:x:update";

    public override string Name => "XEP-0153: vCard-Based Avatars";
    public override string FeatureUri => NsVCardUpdate;

    public string? CurrentAvatarHash { get; set; }

    public event Action<Jid, string?>? AvatarHashReceived;

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name != "presence")
        {
            return ValueTask.FromResult(true);
        }

        var fromStr = element.GetAttr("from");
        if (string.IsNullOrEmpty(fromStr) || !Jid.TryParse(fromStr, out var fromJid))
        {
            return ValueTask.FromResult(true);
        }

        var updateX = element.Element("x", NsVCardUpdate);
        if (updateX is not null)
        {
            var photoElem = updateX.Element("photo");
            if (photoElem is not null)
            {
                var hash = photoElem.Value?.Trim() ?? string.Empty;
                AvatarHashReceived?.Invoke(fromJid.BareJid, hash);
            }
        }

        return ValueTask.FromResult(true);
    }

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "presence" && element.GetAttr("type") != "error")
        {
            if (CurrentAvatarHash is not null)
            {
                var existing = element.Element("x", NsVCardUpdate);
                if (existing is not null)
                {
                    element.RemoveChild(existing);
                }

                var updateX = new XmppElement("x", NsVCardUpdate);
                var photoElem = new XmppElement("photo");
                if (!string.IsNullOrEmpty(CurrentAvatarHash))
                {
                    photoElem.Value = CurrentAvatarHash;
                }
                updateX.Child(photoElem);
                element.Child(updateX);
            }
        }

        return ValueTask.FromResult(true);
    }

    public async Task<(byte[] Data, string MimeType)?> FetchVCardAvatarAsync(Jid? targetJid = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = targetJid is not null ? IqStanza.CreateGet(targetJid.BareJid) : IqStanza.CreateGet();
        iq.RawElement.Child(new XmppElement("vCard", NsVCard));

        try
        {
            var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
            if (resultIq.IsError) return null;

            var vCard = resultIq.RawElement.Element("vCard", NsVCard);
            var photo = vCard?.Element("PHOTO");
            if (photo is null) return null;

            var binval = photo.Element("BINVAL")?.Value;
            if (string.IsNullOrWhiteSpace(binval)) return null;

            var mime = photo.Element("TYPE")?.Value?.Trim();
            if (string.IsNullOrEmpty(mime)) mime = "image/png";

            var data = Convert.FromBase64String(binval.Trim());
            return (data, mime);
        }
        catch
        {
            // Soft failure fetching vCard
        }

        return null;
    }

    public async Task PublishVCardAvatarAsync(byte[] data, string mimeType, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var iq = IqStanza.CreateSet();
        var vCard = new XmppElement("vCard", NsVCard)
            .Child(new XmppElement("PHOTO")
                .Child(new XmppElement("TYPE") { Value = mimeType })
                .Child(new XmppElement("BINVAL") { Value = Convert.ToBase64String(data) }));

        iq.RawElement.Child(vCard);
        await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task ClearVCardAvatarAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet();
        var vCard = new XmppElement("vCard", NsVCard);
        iq.RawElement.Child(vCard);

        await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
    }
}
