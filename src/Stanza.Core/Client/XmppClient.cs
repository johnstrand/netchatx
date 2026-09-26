using System.Collections.Concurrent;
using System.Text;
using Stanza.Core.Sasl;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;

namespace Stanza.Core.Client;

public sealed class XmppClient : IAsyncDisposable
{
    private readonly XmppClientOptions _options;
    private readonly IXmppTransport _transport;
    private readonly XmppStreamParser _parser = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IqStanza>> _pendingIqs = new();
    private readonly List<IIncomingStanzaFilter> _incomingFilters = [];
    private readonly List<IOutgoingStanzaFilter> _outgoingFilters = [];
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private const int MaxEarlyMessageBufferSize = 100;

    private CancellationTokenSource? _sessionCts;
    private Task? _readLoopTask;
    private XmppClientState _state = XmppClientState.Disconnected;

    public XmppClientOptions Options => _options;
    public Jid BoundJid { get; private set; }
    public XmppElement? StreamFeatures { get; private set; }
    public XmppClientState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(value);
            }
        }
    }

    public bool IsConnected => State == XmppClientState.Ready || State == XmppClientState.Connected;
    public bool IsReady => State == XmppClientState.Ready;

    public event Action<XmppClientState>? StateChanged;
    private readonly System.Collections.Concurrent.ConcurrentQueue<MessageStanza> _earlyMessageBuffer = new();
    private Func<MessageStanza, Task>? _messageReceived;

    public event Func<MessageStanza, Task>? MessageReceived
    {
        add
        {
            _messageReceived += value;
            while (_earlyMessageBuffer.TryDequeue(out var msg))
            {
                _ = value?.Invoke(msg);
            }
        }
        remove => _messageReceived -= value;
    }

    public event Func<PresenceStanza, Task>? PresenceReceived;
    public event Func<IqStanza, Task>? IqReceived;
    public event Func<XmppElement, Task>? ElementReceived;
    public event Action<Exception?>? Disconnected;

    public XmppClient(XmppClientOptions options, IXmppTransport? transport = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? new TcpTlsTransport(options.AllowUntrustedCertificates);
        BoundJid = options.Jid;
    }

    public void AddIncomingFilter(IIncomingStanzaFilter filter) => _incomingFilters.Add(filter);
    public void AddOutgoingFilter(IOutgoingStanzaFilter filter) => _outgoingFilters.Add(filter);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (State != XmppClientState.Disconnected)
            throw new InvalidOperationException($"Cannot connect while in state {State}");

        State = XmppClientState.Connecting;
        _sessionCts?.Dispose();
        _sessionCts = new CancellationTokenSource();
        StreamFeatures = null;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts.Token);

        var host = _options.Host ?? _options.Jid.Domain;
        await _transport.ConnectAsync(host, _options.Port, linkedCts.Token);

        State = XmppClientState.Connected;

        if (_options.UseDirectTls && !_transport.IsSecure)
        {
            await _transport.UpgradeToTlsAsync(host, linkedCts.Token);
        }

        // Initiate stream negotiation
        await NegotiateStreamAsync(linkedCts.Token);

        // Start background reading pump
        _readLoopTask = Task.Run(() => RunReadLoopAsync(_sessionCts.Token));
    }

    private async Task NegotiateStreamAsync(CancellationToken cancellationToken)
    {
        // Step 1: Open stream
        _parser.Reset();
        await SendStreamHeaderAsync(cancellationToken);

        // Read stream header and features
        var streamHeader = await ReadNextElementAsync(cancellationToken);
        var features = await ReadNextElementAsync(cancellationToken);
        StreamFeatures = features;

        // Step 2: StartTLS if available and not direct TLS
        if (!_transport.IsSecure)
        {
            var startTls = features.Element("starttls", "urn:ietf:params:xml:ns:xmpp-tls");
            if (startTls is not null)
            {
                State = XmppClientState.StartingTls;
                var startTlsElem = new XmppElement("starttls", "urn:ietf:params:xml:ns:xmpp-tls");
                await SendElementRawAsync(startTlsElem, cancellationToken);

                var response = await ReadNextElementAsync(cancellationToken);
                if (response.Name == "proceed")
                {
                    var targetHost = _options.Host ?? _options.Jid.Domain;
                    await _transport.UpgradeToTlsAsync(targetHost, cancellationToken);

                    // Re-open stream over TLS
                    _parser.Reset();
                    await SendStreamHeaderAsync(cancellationToken);
                    _ = await ReadNextElementAsync(cancellationToken); // stream header
                    features = await ReadNextElementAsync(cancellationToken);
                    StreamFeatures = features;
                }
                else
                {
                    throw new InvalidOperationException($"StartTLS failed: {response.ToXmlString()}");
                }
            }
        }

        // Step 3: SASL Authentication
        State = XmppClientState.Authenticating;
        var mechanismsElem = features.Element("mechanisms", "urn:ietf:params:xml:ns:xmpp-sasl");
        if (mechanismsElem is null)
            throw new InvalidOperationException("No SASL mechanisms offered by server.");

        var mechanisms = mechanismsElem.Elements("mechanism").Select(m => m.Value?.Trim()).Where(m => m is not null).ToHashSet();

        ISaslMechanism saslMech;
        if (mechanisms.Contains("SCRAM-SHA-256"))
            saslMech = new ScramSaslMechanism(isSha256: true);
        else if (mechanisms.Contains("SCRAM-SHA-1"))
            saslMech = new ScramSaslMechanism(isSha256: false);
        else if (mechanisms.Contains("PLAIN"))
            saslMech = new PlainSaslMechanism();
        else
            throw new NotSupportedException($"No supported SASL mechanism found in: {string.Join(", ", mechanisms)}");

        var username = _options.Jid.LocalPart ?? _options.Jid.Domain;
        var initialPayload = saslMech.CreateInitialResponse(username, _options.Password);

        var authElem = new XmppElement("auth", "urn:ietf:params:xml:ns:xmpp-sasl")
            .Attr("mechanism", saslMech.Name);

        if (!string.IsNullOrEmpty(initialPayload))
            authElem.Value = initialPayload;

        await SendElementRawAsync(authElem, cancellationToken);

        while (true)
        {
            var saslResp = await ReadNextElementAsync(cancellationToken);
            if (saslResp.Name == "challenge")
            {
                var challengeResp = saslMech.HandleChallenge(saslResp.Value ?? "", _options.Password);
                var respElem = new XmppElement("response", "urn:ietf:params:xml:ns:xmpp-sasl")
                {
                    Value = challengeResp ?? ""
                };
                await SendElementRawAsync(respElem, cancellationToken);
            }
            else if (saslResp.Name == "success")
            {
                if (!saslMech.VerifySuccess(saslResp.Value))
                    throw new InvalidOperationException("SASL server signature verification failed.");
                break;
            }
            else if (saslResp.Name == "failure")
            {
                throw new InvalidOperationException($"SASL authentication failed: {saslResp.ToXmlString()}");
            }
            else
            {
                throw new InvalidOperationException($"Unexpected SASL element: {saslResp.Name}");
            }
        }

        State = XmppClientState.Authenticated;

        // Step 4: Re-open stream post-auth
        _parser.Reset();
        await SendStreamHeaderAsync(cancellationToken);
        _ = await ReadNextElementAsync(cancellationToken); // stream header
        features = await ReadNextElementAsync(cancellationToken);
        StreamFeatures = features;

        // Step 5: Resource Binding
        State = XmppClientState.BindingResource;
        var bindElem = features.Element("bind", "urn:ietf:params:xml:ns:xmpp-bind");
        if (bindElem is null)
            throw new InvalidOperationException("Server did not advertise xmpp-bind feature.");

        var bindIqId = Guid.NewGuid().ToString("N");
        var bindIq = new IqStanza(IqStanza.TypeSet, id: bindIqId);
        var bindReq = new XmppElement("bind", "urn:ietf:params:xml:ns:xmpp-bind");
        if (!string.IsNullOrEmpty(_options.Resource))
        {
            bindReq.Child(new XmppElement("resource") { Value = _options.Resource });
        }
        bindIq.RawElement.Child(bindReq);
        await SendElementRawAsync(bindIq.RawElement, cancellationToken);

        var bindResult = await ReadNextElementAsync(cancellationToken);
        if (bindResult.GetAttr("type") != "result")
            throw new InvalidOperationException($"Resource binding failed: {bindResult.ToXmlString()}");

        var boundJidStr = bindResult.Element("bind")?.Element("jid")?.Value;
        if (!string.IsNullOrEmpty(boundJidStr))
        {
            BoundJid = Jid.Parse(boundJidStr);
        }

        // Step 6: Session Establishment (legacy RFC 3921 support if required)
        var sessionElem = features.Element("session", "urn:ietf:params:xml:ns:xmpp-session");
        if (sessionElem is not null && !sessionElem.HasAttr("optional"))
        {
            var sessionIqId = Guid.NewGuid().ToString("N");
            var sessionIq = new IqStanza(IqStanza.TypeSet, id: sessionIqId);
            sessionIq.RawElement.Child(new XmppElement("session", "urn:ietf:params:xml:ns:xmpp-session"));
            await SendElementRawAsync(sessionIq.RawElement, cancellationToken);
            _ = await ReadNextElementAsync(cancellationToken);
        }

        State = XmppClientState.Ready;
    }

    private ValueTask<XmppElement> ReadNextElementAsync(CancellationToken cancellationToken)
    {
        return _parser.ReadElementAsync(_transport.Input, cancellationToken);
    }

    private async Task SendStreamHeaderAsync(CancellationToken cancellationToken)
    {
        var header = $"<?xml version='1.0'?><stream:stream to='{_options.Jid.Domain}' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' version='1.0'>";
        var bytes = Encoding.UTF8.GetBytes(header);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _transport.Output.WriteAsync(bytes, cancellationToken);
            await _transport.Output.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendElementAsync(XmppElement element, CancellationToken cancellationToken = default)
    {
        foreach (var filter in _outgoingFilters)
        {
            var proceed = await filter.OnOutgoingElementAsync(this, element, cancellationToken);
            if (!proceed) return;
        }

        await SendElementRawAsync(element, cancellationToken);
    }

    public async Task SendStanzaAsync(XmppStanza stanza, CancellationToken cancellationToken = default)
    {
        stanza.SyncAttributes();
        await SendElementAsync(stanza.RawElement, cancellationToken);
    }

    public async Task<IqStanza> SendIqAsync(IqStanza iq, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (State == XmppClientState.Disconnected || State == XmppClientState.Disconnecting)
            throw new InvalidOperationException($"Cannot send IQ while client is {State}.");

        if (string.IsNullOrEmpty(iq.Id))
            iq.Id = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingIqs[iq.Id] = tcs;

        try
        {
            await SendStanzaAsync(iq, cancellationToken);
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        finally
        {
            _pendingIqs.TryRemove(iq.Id, out _);
        }
    }

    private async Task SendElementRawAsync(XmppElement element, CancellationToken cancellationToken)
    {
        var xml = element.ToXmlString();
        var bytes = Encoding.UTF8.GetBytes(xml);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _transport.Output.WriteAsync(bytes, cancellationToken);
            await _transport.Output.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? disconnectReason = null;
        try
        {
            await foreach (var elem in _parser.ReadAllAsync(_transport.Input, cancellationToken))
            {
                if (elem.FullName == "stream:stream" && elem.GetAttr("closed") == "true")
                {
                    break;
                }

                // Run incoming filters
                var pass = true;
                foreach (var filter in _incomingFilters)
                {
                    if (!await filter.OnIncomingElementAsync(this, elem, cancellationToken))
                    {
                        pass = false;
                        break;
                    }
                }

                if (!pass) continue;

                // Stanza routing
                if (elem.Name == "iq")
                {
                    var iq = new IqStanza(elem);
                    if (!string.IsNullOrEmpty(iq.Id) && (iq.IsResult || iq.IsError))
                    {
                        if (_pendingIqs.TryRemove(iq.Id, out var tcs))
                        {
                            tcs.TrySetResult(iq);
                            continue;
                        }
                    }

                    if (IqReceived is not null)
                        _ = IqReceived(iq);
                }
                else if (elem.Name == "message")
                {
                    var msg = new MessageStanza(elem);
                    if (_messageReceived is not null)
                        _ = _messageReceived(msg);
                    else if (_earlyMessageBuffer.Count < MaxEarlyMessageBufferSize)
                        _earlyMessageBuffer.Enqueue(msg);
                }
                else if (elem.Name == "presence")
                {
                    var pres = new PresenceStanza(elem);
                    if (PresenceReceived is not null)
                        _ = PresenceReceived(pres);
                }

                if (ElementReceived is not null)
                    _ = ElementReceived(elem);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            disconnectReason = ex;
            System.Diagnostics.Debug.WriteLine($"Read loop terminated: {ex}");
        }
        finally
        {
            State = XmppClientState.Disconnected;
            try { await _transport.CloseAsync(); } catch { }
            foreach (var (_, tcs) in _pendingIqs)
            {
                tcs.TrySetException(new IOException("Connection closed."));
            }
            _pendingIqs.Clear();
            Disconnected?.Invoke(disconnectReason);
        }
    }

    public async Task DisconnectAsync()
    {
        if (State == XmppClientState.Disconnected)
            return;

        State = XmppClientState.Disconnecting;

        try
        {
            await _sendLock.WaitAsync();
            try
            {
                // Send </stream:stream>
                var closeTag = "</stream:stream>"u8.ToArray();
                await _transport.Output.WriteAsync(closeTag);
                await _transport.Output.FlushAsync();
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex)
        {
            // Transport errors during disconnect are expected if the underlying connection or socket is already broken/closed.
            System.Diagnostics.Debug.WriteLine($"Error sending stream close tag during disconnect: {ex.Message}");
        }

        _sessionCts?.Cancel();
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask; } catch { }
        }

        await _transport.CloseAsync();
        State = XmppClientState.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sessionCts?.Dispose();
        _sendLock.Dispose();
        await _transport.DisposeAsync();
    }
}
