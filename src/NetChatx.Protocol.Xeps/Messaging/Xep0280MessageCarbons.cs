using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Messaging;

public sealed class Xep0280MessageCarbons : XepFeatureBase
{
    public const string NsCarbons = "urn:xmpp:carbons:2";
    public const string NsForward = "urn:xmpp:forward:0";

    public override string Name => "XEP-0280: Message Carbons";
    public override string FeatureUri => NsCarbons;

    public bool IsEnabled { get; private set; }

    public event Action<MessageStanza, bool>? CarbonMessageReceived; // message, isSentByUs

    public async Task EnableAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet();
        iq.RawElement.Child(new XmppElement("enable", NsCarbons));

        var res = await Client.SendIqAsync(iq, cancellationToken: ct);
        if (res.IsResult)
        {
            IsEnabled = true;
        }
    }

    public async Task DisableAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet();
        iq.RawElement.Child(new XmppElement("disable", NsCarbons));

        var res = await Client.SendIqAsync(iq, cancellationToken: ct);
        if (res.IsResult)
        {
            IsEnabled = false;
        }
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var receivedCarbon = element.Element("received", NsCarbons);
            var sentCarbon = element.Element("sent", NsCarbons);

            var carbonWrapper = receivedCarbon ?? sentCarbon;
            if (carbonWrapper is not null)
            {
                var forwarded = carbonWrapper.Element("forwarded", NsForward);
                var innerMsgElem = forwarded?.Element("message");
                if (innerMsgElem is not null)
                {
                    var innerMsg = new MessageStanza(innerMsgElem);
                    var isSentByUs = sentCarbon is not null;
                    CarbonMessageReceived?.Invoke(innerMsg, isSentByUs);
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
