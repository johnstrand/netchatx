using NetChatx.Core.Client;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Resilience;

public sealed class Xep0198StreamManagement : XepFeatureBase
{
    public const string NsSm = "urn:xmpp:sm:3";

    public override string Name => "XEP-0198: Stream Management";
    public override string FeatureUri => NsSm;

    public bool IsEnabled { get; private set; }
    public string? ResumeId { get; private set; }
    public uint InboundHandled { get; private set; }
    public uint OutboundHandled { get; private set; }

    public event Action<uint>? AckReceived;

    public async Task EnableAsync(bool allowResume = true, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var elem = new XmppElement("enable", NsSm);
        if (allowResume)
        {
            elem.Attr("resume", "true");
        }

        await Client.SendElementAsync(elem, ct);
    }

    public async Task RequestAckAsync(CancellationToken ct = default)
    {
        if (Client is null || !IsEnabled) return;
        var r = new XmppElement("r", NsSm);
        await Client.SendElementAsync(r, ct);
    }

    public async Task SendAckAsync(CancellationToken ct = default)
    {
        if (Client is null || !IsEnabled) return;
        var a = new XmppElement("a", NsSm).Attr("h", InboundHandled.ToString());
        await Client.SendElementAsync(a, ct);
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Namespace == NsSm)
        {
            if (element.Name == "enabled")
            {
                IsEnabled = true;
                ResumeId = element.GetAttr("id");
                return false;
            }
            if (element.Name == "r")
            {
                // Server requested ack
                await SendAckAsync(cancellationToken);
                return false;
            }
            if (element.Name == "a")
            {
                // Server answered ack
                if (uint.TryParse(element.GetAttr("h"), out uint ackH))
                {
                    AckReceived?.Invoke(ackH);
                }
                return false;
            }
            if (element.Name == "failed")
            {
                IsEnabled = false;
                return false;
            }
        }

        // Count stanzas for stream management (RFC 6120: iq, message, presence)
        if (IsEnabled && element.Name is "iq" or "message" or "presence")
        {
            unchecked { InboundHandled++; }
        }

        return true;
    }

    public override ValueTask<bool> OnOutgoingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (IsEnabled && element.Name is "iq" or "message" or "presence")
        {
            unchecked { OutboundHandled++; }
        }

        return ValueTask.FromResult(true);
    }
}
