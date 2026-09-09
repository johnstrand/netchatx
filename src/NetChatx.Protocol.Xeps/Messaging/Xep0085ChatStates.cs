using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public enum ChatState
{
    Active,
    Composing,
    Paused,
    Inactive,
    Gone
}

public sealed class Xep0085ChatStates : XepFeatureBase
{
    public const string NsChatStates = "http://jabber.org/protocol/chatstates";

    public override string Name => "XEP-0085: Chat State Notifications";
    public override string FeatureUri => NsChatStates;

    public event Action<Jid, ChatState>? ChatStateReceived;

    public async Task SendChatStateAsync(Jid to, ChatState state, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        string stateName = state switch
        {
            ChatState.Active => "active",
            ChatState.Composing => "composing",
            ChatState.Paused => "paused",
            ChatState.Inactive => "inactive",
            ChatState.Gone => "gone",
            _ => "active"
        };

        var msg = new MessageStanza(to: to, type: MessageStanza.TypeChat);
        msg.RawElement.Child(new XmppElement(stateName, NsChatStates));

        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            string? fromStr = element.GetAttr("from");
            if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
            {
                foreach (var child in element.Children)
                {
                    if (child.Namespace == NsChatStates)
                    {
                        var state = child.Name switch
                        {
                            "active" => ChatState.Active,
                            "composing" => ChatState.Composing,
                            "paused" => ChatState.Paused,
                            "inactive" => ChatState.Inactive,
                            "gone" => ChatState.Gone,
                            _ => (ChatState?)null
                        };

                        if (state.HasValue)
                        {
                            ChatStateReceived?.Invoke(fromJid, state.Value);
                        }
                    }
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
