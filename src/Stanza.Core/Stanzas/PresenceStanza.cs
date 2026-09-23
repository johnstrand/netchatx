using Stanza.Core.Xml;

namespace Stanza.Core.Stanzas;

public sealed class PresenceStanza : XmppStanza
{
    public const string TypeUnavailable = "unavailable";
    public const string TypeSubscribe = "subscribe";
    public const string TypeSubscribed = "subscribed";
    public const string TypeUnsubscribe = "unsubscribe";
    public const string TypeUnsubscribed = "unsubscribed";
    public const string TypeError = "error";

    public const string ShowChat = "chat";
    public const string ShowAway = "away";
    public const string ShowDnd = "dnd";
    public const string ShowXa = "xa";

    public bool IsAvailable => Type is null || !string.Equals(Type, TypeUnavailable, StringComparison.OrdinalIgnoreCase);

    public string? Show
    {
        get => RawElement.Element("show")?.Value;
        set
        {
            var old = RawElement.Element("show");
            if (value is null)
            {
                if (old is not null) RawElement.RemoveChild(old);
            }
            else
            {
                if (old is null)
                {
                    old = new XmppElement("show");
                    RawElement.Child(old);
                }
                old.Value = value;
            }
        }
    }

    public string? Status
    {
        get => RawElement.Element("status")?.Value;
        set
        {
            var old = RawElement.Element("status");
            if (value is null)
            {
                if (old is not null) RawElement.RemoveChild(old);
            }
            else
            {
                if (old is null)
                {
                    old = new XmppElement("status");
                    RawElement.Child(old);
                }
                old.Value = value;
            }
        }
    }

    public int? Priority
    {
        get
        {
            var val = RawElement.Element("priority")?.Value;
            return int.TryParse(val, out int p) ? p : null;
        }
        set
        {
            var old = RawElement.Element("priority");
            if (value is null)
            {
                if (old is not null) RawElement.RemoveChild(old);
            }
            else
            {
                if (old is null)
                {
                    old = new XmppElement("priority");
                    RawElement.Child(old);
                }
                old.Value = value.Value.ToString();
            }
        }
    }

    public PresenceStanza(XmppElement element) : base(element)
    {
    }

    public PresenceStanza(
        string? type = null,
        string? show = null,
        string? status = null,
        int? priority = null,
        Jid? to = null,
        Jid? from = null,
        string? id = null)
        : base("presence", type, to, from, id)
    {
        if (show is not null) Show = show;
        if (status is not null) Status = status;
        if (priority is not null) Priority = priority;
    }

    public static PresenceStanza Available(string? show = null, string? status = null, int priority = 0)
        => new(type: null, show: show, status: status, priority: priority);

    public static PresenceStanza Unavailable(string? status = null)
        => new(type: TypeUnavailable, status: status);
}
