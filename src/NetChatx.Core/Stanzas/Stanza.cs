using NetChatx.Core.Xml;

namespace NetChatx.Core.Stanzas;

/// <summary>
/// Abstract base class for top-level XMPP stanzas (RFC 6120: iq, message, presence).
/// </summary>
public abstract class Stanza
{
    public string? Id { get; set; }
    public Jid? To { get; set; }
    public Jid? From { get; set; }
    public string? Type { get; set; }
    public XmppElement RawElement { get; protected set; }

    protected Stanza(XmppElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        RawElement = element;
        Id = element.GetAttr("id");
        Type = element.GetAttr("type");

        string? toStr = element.GetAttr("to");
        if (!string.IsNullOrEmpty(toStr) && Jid.TryParse(toStr, out var toJid))
            To = toJid;

        string? fromStr = element.GetAttr("from");
        if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
            From = fromJid;
    }

    protected Stanza(string name, string? type = null, Jid? to = null, Jid? from = null, string? id = null)
    {
        RawElement = new XmppElement(name);
        Type = type;
        To = to;
        From = from;
        Id = id ?? Guid.NewGuid().ToString();

        SyncAttributes();
    }

    public virtual void SyncAttributes()
    {
        if (From is not null)
            RawElement.Attr("from", From.ToString());
        else
            RawElement.Attr("from", null);

        RawElement.Attr("id", Id);
        RawElement.Attr("to", To?.ToString());

        if (RawElement.Name == "message" && !RawElement.HasAttr("xml:lang"))
        {
            RawElement.Attr("xml:lang", "en");
        }

        RawElement.Attr("type", Type);
        RawElement.Attr("xmlns", "jabber:client");
    }

    public string ToXmlString(bool indent = false)
    {
        SyncAttributes();
        return RawElement.ToXmlString(indent);
    }
}
