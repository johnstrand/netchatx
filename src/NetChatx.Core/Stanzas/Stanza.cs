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
        Id = id ?? Guid.NewGuid().ToString("N");

        SyncAttributes();
    }

    public virtual void SyncAttributes()
    {
        RawElement.Attr("id", Id);
        RawElement.Attr("type", Type);
        RawElement.Attr("to", To?.ToString());
        RawElement.Attr("from", From?.ToString());
    }

    public string ToXmlString(bool indent = false)
    {
        SyncAttributes();
        return RawElement.ToXmlString(indent);
    }
}
