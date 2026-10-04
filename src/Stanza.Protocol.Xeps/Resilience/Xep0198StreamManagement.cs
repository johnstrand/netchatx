using Stanza.Core.Client;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Resilience;

public sealed class Xep0198StreamManagement : XepFeatureBase, IStreamResumptionHandler
{
    public const string NsSm = "urn:xmpp:sm:3";

    public override string Name => "XEP-0198: Stream Management";
    public override string FeatureUri => NsSm;

    private readonly object _lock = new();
    private readonly List<(uint Seq, XmppElement Stanza)> _unacknowledgedStanzas = [];
    private bool _isRetransmitting;

    public bool IsEnabled { get; private set; }
    public string? ResumeId { get; private set; }
    public uint InboundHandled { get; private set; }
    public uint OutboundHandled { get; private set; }
    public uint LastAckedByServer { get; internal set; }

    public bool CanResume
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(ResumeId);
            }
        }
    }

    public int UnacknowledgedCount
    {
        get
        {
            lock (_lock)
            {
                return _unacknowledgedStanzas.Count;
            }
        }
    }

    public IReadOnlyList<XmppElement> UnacknowledgedStanzas
    {
        get
        {
            lock (_lock)
            {
                return _unacknowledgedStanzas.Select(x => x.Stanza).ToList();
            }
        }
    }

    public bool AutoEnableWhenAdvertised { get; set; } = true;

    public event Action<uint>? AckReceived;
    public event Action<IReadOnlyList<XmppElement>>? UnrecoverableMessagesFailed;
    public event Action<IReadOnlyList<XmppElement>>? ResumedAndRetransmitted;

    public override ValueTask AttachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        base.AttachAsync(client, cancellationToken);
        client.ResumptionHandler = this;
        return ValueTask.CompletedTask;
    }

    public override ValueTask DetachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        if (client.ResumptionHandler == this)
        {
            client.ResumptionHandler = null;
        }
        return base.DetachAsync(client, cancellationToken);
    }

    public async Task EnableAsync(bool allowResume = true, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var elem = new XmppElement("enable", NsSm);
        if (allowResume)
        {
            elem.Attr("resume", "true");
        }

        await Client.SendElementAsync(elem, ct).ConfigureAwait(false);
    }

    public async Task RequestAckAsync(CancellationToken ct = default)
    {
        if (Client is null || !IsEnabled) return;
        var r = new XmppElement("r", NsSm);
        await Client.SendElementAsync(r, ct).ConfigureAwait(false);
    }

    public async Task SendAckAsync(CancellationToken ct = default)
    {
        if (Client is null || !IsEnabled) return;
        var a = new XmppElement("a", NsSm).Attr("h", InboundHandled.ToString());
        await Client.SendElementAsync(a, ct).ConfigureAwait(false);
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Namespace == NsSm)
        {
            if (element.Name == "enabled")
            {
                lock (_lock)
                {
                    IsEnabled = true;
                    ResumeId = element.GetAttr("id");
                }
                return false;
            }
            if (element.Name == "r")
            {
                // Server requested ack
                await SendAckAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
            if (element.Name == "a")
            {
                // Server answered ack
                if (uint.TryParse(element.GetAttr("h"), out uint ackH))
                {
                    HandleAck(ackH);
                }
                return false;
            }
            if (element.Name == "failed")
            {
                lock (_lock)
                {
                    IsEnabled = false;
                }
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
        if (_isRetransmitting)
        {
            return ValueTask.FromResult(true);
        }

        if (IsEnabled && element.Name is "iq" or "message" or "presence")
        {
            lock (_lock)
            {
                unchecked { OutboundHandled++; }
                _unacknowledgedStanzas.Add((OutboundHandled, element.Clone()));
            }
        }

        return ValueTask.FromResult(true);
    }

    public void HandleAck(uint ackH)
    {
        lock (_lock)
        {
            int delta = unchecked((int)(ackH - LastAckedByServer));
            if (delta > 0)
            {
                int toRemove = Math.Min(delta, _unacknowledgedStanzas.Count);
                if (toRemove > 0)
                {
                    _unacknowledgedStanzas.RemoveRange(0, toRemove);
                }
                LastAckedByServer = ackH;
            }
        }
        AckReceived?.Invoke(ackH);
    }

    public async Task<bool> TryResumeAsync(
        XmppClient client,
        XmppElement streamFeatures,
        Func<XmppElement, Task> sendRawAsync,
        Func<Task<XmppElement>> readNextElementAsync,
        CancellationToken cancellationToken)
    {
        string? resumeId;
        uint inboundHandled;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(ResumeId)) return false;
            resumeId = ResumeId;
            inboundHandled = InboundHandled;
        }

        var smFeature = streamFeatures.Element("sm", NsSm);
        if (smFeature is null)
        {
            return false;
        }

        var resumeElem = new XmppElement("resume", NsSm)
            .Attr("previd", resumeId)
            .Attr("h", inboundHandled.ToString());

        await sendRawAsync(resumeElem).ConfigureAwait(false);

        var response = await readNextElementAsync().ConfigureAwait(false);
        if (response.Namespace == NsSm && response.Name == "resumed")
        {
            var prevId = response.GetAttr("previd");
            if (uint.TryParse(response.GetAttr("h"), out uint serverHandledH))
            {
                HandleAck(serverHandledH);
            }

            lock (_lock)
            {
                IsEnabled = true;
                if (!string.IsNullOrEmpty(prevId))
                {
                    ResumeId = prevId;
                }
            }

            List<XmppElement> toRetransmit;
            lock (_lock)
            {
                toRetransmit = _unacknowledgedStanzas.Select(s => s.Stanza).ToList();
            }

            _isRetransmitting = true;
            try
            {
                foreach (var stanza in toRetransmit)
                {
                    await sendRawAsync(stanza).ConfigureAwait(false);
                }
            }
            finally
            {
                _isRetransmitting = false;
            }

            ResumedAndRetransmitted?.Invoke(toRetransmit);
            return true;
        }

        return false;
    }

    public Task OnResumptionFailedAsync(XmppClient client, CancellationToken cancellationToken)
    {
        List<XmppElement> unrecoverable;
        lock (_lock)
        {
            unrecoverable = _unacknowledgedStanzas.Select(s => s.Stanza).ToList();
            ResetResumptionState();
        }

        if (unrecoverable.Count > 0)
        {
            UnrecoverableMessagesFailed?.Invoke(unrecoverable);
        }

        return Task.CompletedTask;
    }

    public async Task OnStreamNegotiatedAsync(
        XmppClient client,
        XmppElement streamFeatures,
        Func<XmppElement, Task> sendRawAsync,
        Func<Task<XmppElement>> readNextElementAsync,
        CancellationToken cancellationToken)
    {
        if (!AutoEnableWhenAdvertised) return;

        var smFeature = streamFeatures.Element("sm", NsSm);
        if (smFeature is null)
        {
            lock (_lock)
            {
                IsEnabled = false;
                ResumeId = null;
            }
            return;
        }

        var enableElem = new XmppElement("enable", NsSm).Attr("resume", "true");
        await sendRawAsync(enableElem).ConfigureAwait(false);

        var response = await readNextElementAsync().ConfigureAwait(false);
        if (response.Namespace == NsSm && response.Name == "enabled")
        {
            lock (_lock)
            {
                IsEnabled = true;
                ResumeId = response.GetAttr("id");
                InboundHandled = 0;
                OutboundHandled = 0;
                LastAckedByServer = 0;
                _unacknowledgedStanzas.Clear();
            }
        }
        else
        {
            lock (_lock)
            {
                IsEnabled = false;
                ResumeId = null;
            }
        }
    }

    public void OnCleanDisconnect()
    {
        ResetResumptionState();
    }

    public void ResetResumptionState()
    {
        lock (_lock)
        {
            IsEnabled = false;
            ResumeId = null;
            InboundHandled = 0;
            OutboundHandled = 0;
            LastAckedByServer = 0;
            _unacknowledgedStanzas.Clear();
        }
    }
}
