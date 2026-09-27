using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

public sealed class ReactionEventArgs : EventArgs
{
    public required string TargetMessageId { get; init; }
    public required Jid SenderJid { get; init; }
    public required Jid RemoteJid { get; init; }
    public required IReadOnlyList<string> Emojis { get; init; }
    public bool IsGroupChat { get; init; }
    public bool IsCarbonSent { get; init; }
}

public sealed class Xep0444Reactions : XepFeatureBase
{
    public const string NsReactions = "urn:xmpp:reactions:0";
    public const string NsCarbons = "urn:xmpp:carbons:2";
    public const string NsForward = "urn:xmpp:forward:0";

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

        await Client.SendStanzaAsync(msg, ct).ConfigureAwait(false);
    }

    public static bool TryExtractReaction(XmppElement messageElem, bool isCarbonSent, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ReactionEventArgs? args)
    {
        args = null;
        var reactionsElem = messageElem.Element("reactions", NsReactions);
        if (reactionsElem is null) return false;

        var targetId = reactionsElem.GetAttr("id");
        if (string.IsNullOrEmpty(targetId)) return false;

        var fromStr = messageElem.GetAttr("from");
        var toStr = messageElem.GetAttr("to");
        var msgType = messageElem.GetAttr("type");
        var isGroupChat = string.Equals(msgType, MessageStanza.TypeGroupChat, StringComparison.OrdinalIgnoreCase);

        Jid senderJid;
        Jid remoteJid;

        if (isCarbonSent)
        {
            if (string.IsNullOrEmpty(toStr) || !Jid.TryParse(toStr, out var toJid)) return false;
            if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
                senderJid = fromJid;
            else
                senderJid = toJid;

            remoteJid = toJid.BareJid;
        }
        else
        {
            if (string.IsNullOrEmpty(fromStr) || !Jid.TryParse(fromStr, out var fromJid)) return false;
            senderJid = fromJid;
            remoteJid = isGroupChat ? fromJid.BareJid : fromJid.BareJid;
        }

        var emojiList = reactionsElem.Elements("reaction")
            .Select(r => r.Value)
            .Where(val => !string.IsNullOrWhiteSpace(val))
            .Select(val => val!)
            .Distinct()
            .ToList();

        args = new ReactionEventArgs
        {
            TargetMessageId = targetId,
            SenderJid = senderJid,
            RemoteJid = remoteJid,
            Emojis = emojiList,
            IsGroupChat = isGroupChat,
            IsCarbonSent = isCarbonSent
        };
        return true;
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            // 1. Direct reaction message
            if (TryExtractReaction(element, isCarbonSent: false, out var directArgs))
            {
                ReactionReceived?.Invoke(directArgs);
            }
            else
            {
                // 2. Check for XEP-0280 Carbons wrapper
                var receivedCarbon = element.Element("received", NsCarbons);
                var sentCarbon = element.Element("sent", NsCarbons);
                var carbonWrapper = receivedCarbon ?? sentCarbon;
                if (carbonWrapper is not null)
                {
                    var forwarded = carbonWrapper.Element("forwarded", NsForward);
                    var innerMsg = forwarded?.Element("message");
                    if (innerMsg is not null && TryExtractReaction(innerMsg, isCarbonSent: sentCarbon is not null, out var carbonArgs))
                    {
                        ReactionReceived?.Invoke(carbonArgs);
                    }
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}

