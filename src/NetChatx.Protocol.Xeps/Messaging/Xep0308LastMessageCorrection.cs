using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public sealed class Xep0308LastMessageCorrection : XepFeatureBase
{
    public const string NsCorrection = "urn:xmpp:message-correct:0";

    public override string Name => "XEP-0308: Last Message Correction";
    public override string FeatureUri => NsCorrection;

    public event Action<MessageStanza, string>? MessageCorrected; // message, originalId

    public async Task SendCorrectionAsync(Jid to, string originalMessageId, string newBody, string type = MessageStanza.TypeChat, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var msg = new MessageStanza(to: to, body: newBody, type: type);
        msg.RawElement.Child(new XmppElement("replace", NsCorrection).Attr("id", originalMessageId));

        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var replaceElem = element.Element("replace", NsCorrection);
            if (replaceElem is not null)
            {
                string? originalId = replaceElem.GetAttr("id");
                if (!string.IsNullOrEmpty(originalId))
                {
                    var msg = new MessageStanza(element);
                    MessageCorrected?.Invoke(msg, originalId);
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
