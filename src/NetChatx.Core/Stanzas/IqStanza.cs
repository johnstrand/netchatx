using NetChatx.Core.Xml;

namespace NetChatx.Core.Stanzas;

public sealed class IqStanza : Stanza
{
    public const string TypeGet = "get";
    public const string TypeSet = "set";
    public const string TypeResult = "result";
    public const string TypeError = "error";

    public bool IsGet => string.Equals(Type, TypeGet, StringComparison.OrdinalIgnoreCase);
    public bool IsSet => string.Equals(Type, TypeSet, StringComparison.OrdinalIgnoreCase);
    public bool IsResult => string.Equals(Type, TypeResult, StringComparison.OrdinalIgnoreCase);
    public bool IsError => string.Equals(Type, TypeError, StringComparison.OrdinalIgnoreCase);

    public XmppElement? Query
    {
        get => RawElement.Element("query");
        set
        {
            var old = RawElement.Element("query");
            if (old is not null)
                RawElement.RemoveChild(old);

            if (value is not null)
                RawElement.Child(value);
        }
    }

    public IqStanza(XmppElement element) : base(element)
    {
    }

    public IqStanza(string type, Jid? to = null, Jid? from = null, string? id = null)
        : base("iq", type, to, from, id)
    {
    }

    public static IqStanza CreateGet(Jid? to = null, string? id = null) => new(TypeGet, to, null, id);
    public static IqStanza CreateSet(Jid? to = null, string? id = null) => new(TypeSet, to, null, id);

    public IqStanza CreateResult(XmppElement? payload = null)
    {
        var result = new IqStanza(TypeResult, From, To, Id);
        if (payload is not null)
        {
            result.RawElement.Child(payload);
        }
        return result;
    }

    public IqStanza CreateError(string errorCondition, string? text = null, string errorType = "cancel")
    {
        var errorIq = new IqStanza(TypeError, From, To, Id);
        var errElem = new XmppElement("error")
            .Attr("type", errorType)
            .Child(new XmppElement(errorCondition, "urn:ietf:params:xml:ns:xmpp-stanzas"));

        if (!string.IsNullOrEmpty(text))
        {
            errElem.Child(new XmppElement("text", "urn:ietf:params:xml:ns:xmpp-stanzas") { Value = text });
        }

        errorIq.RawElement.Child(errElem);
        return errorIq;
    }
}
