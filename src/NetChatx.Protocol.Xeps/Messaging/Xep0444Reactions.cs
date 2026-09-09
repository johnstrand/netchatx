using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public sealed class ReactionEventArgs : EventArgs
{
    public required string TargetMessageId { get; init; }
    public required Jid SenderJid { get; init; }
    public required Jid RemoteJid { get; init; }
    public required IReadOnlyList<string> Emojis { get; init; }
}

public sealed class Xep0444Reactions : XepFeatureBase
{
    public const string NsReactions = "urn:xmpp:reactions:0";

    public override string Name => "XEP-0444: Message Reactions";
    public override string FeatureUri => NsReactions;

    public event Action<ReactionEventArgs>? ReactionReceived;

    public async Task SendReactionAsync(Jid to, string targetMessageId, IEnumerable<string> emojis, string type = MessageStanza.TypeChat, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var msg = new MessageStanza(to: to, type: type);
        var reactionsElem = new XmppElement("reactions", NsReactions).Attr("id", targetMessageId);

        foreach (var emoji in emojis.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(emoji))
            {
                reactionsElem.Child(new XmppElement("reaction") { Value = emoji.Trim() });
            }
        }

        msg.RawElement.Child(reactionsElem);
        // Add message processing hint to store
        msg.RawElement.Child(new XmppElement("store", "urn:xmpp:hints"));

        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var reactionsElem = element.Element("reactions", NsReactions);
            if (reactionsElem is not null)
            {
                string? targetId = reactionsElem.GetAttr("id");
                string? fromStr = element.GetAttr("from");

                if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var senderJid))
                {
                    List<string> emojiList = reactionsElem.Elements("reaction")
                        .Select(r => r.Value)
                        .Where(val => !string.IsNullOrWhiteSpace(val))
                        .Select(val => val!)
                        .Distinct()
                        .ToList();

                    var remoteJid = element.GetAttr("type") == MessageStanza.TypeGroupChat ? senderJid.BareJid : senderJid.BareJid;

                    ReactionReceived?.Invoke(new ReactionEventArgs
                    {
                        TargetMessageId = targetId,
                        SenderJid = senderJid,
                        RemoteJid = remoteJid,
                        Emojis = emojiList
                    });
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
