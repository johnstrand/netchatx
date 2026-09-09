using NetChatx.Core.Xml;

namespace NetChatx.Core.Stanzas;

public sealed class MessageStanza : Stanza
{
    public const string TypeChat = "chat";
    public const string TypeGroupChat = "groupchat";
    public const string TypeNormal = "normal";
    public const string TypeHeadline = "headline";
    public const string TypeError = "error";

    public string? Body
    {
        get => RawElement.Element("body")?.Value;
        set
        {
            var bodyElem = RawElement.Element("body");
            if (value is null)
            {
                if (bodyElem is not null)
                    RawElement.RemoveChild(bodyElem);
            }
            else
            {
                if (bodyElem is null)
                {
                    bodyElem = new XmppElement("body");
                    RawElement.Child(bodyElem);
                }
                bodyElem.Value = value;
            }
        }
    }

    public string? Subject
    {
        get => RawElement.Element("subject")?.Value;
        set
        {
            var sub = RawElement.Element("subject");
            if (value is null)
            {
                if (sub is not null) RawElement.RemoveChild(sub);
            }
            else
            {
                if (sub is null)
                {
                    sub = new XmppElement("subject");
                    RawElement.Child(sub);
                }
                sub.Value = value;
            }
        }
    }

    public string? Thread
    {
        get => RawElement.Element("thread")?.Value;
        set
        {
            var th = RawElement.Element("thread");
            if (value is null)
            {
                if (th is not null) RawElement.RemoveChild(th);
            }
            else
            {
                if (th is null)
                {
                    th = new XmppElement("thread");
                    RawElement.Child(th);
                }
                th.Value = value;
            }
        }
    }

    public MessageStanza(XmppElement element) : base(element)
    {
    }

    public MessageStanza(Jid? to = null, string? body = null, string type = TypeChat, Jid? from = null, string? id = null)
        : base("message", type, to, from, id)
    {
        if (body is not null)
            Body = body;
    }

    public static MessageStanza CreateChat(Jid to, string body, Jid? from = null)
        => new(to, body, TypeChat, from);

    public static MessageStanza CreateGroupChat(Jid toRoom, string body, Jid? from = null)
        => new(toRoom, body, TypeGroupChat, from);
}
