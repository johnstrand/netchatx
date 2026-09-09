using System.Text;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Core.Xml;

namespace NetChatx.MockServer;

public sealed class MockXmppServer : IAsyncDisposable
{
    private readonly LoopbackTransport _transport;
    private readonly XmppStreamParser _parser = new();
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public string Domain { get; set; } = "mock.example.com";
    public string ExpectedPassword { get; set; } = "password123";

    public event Action<MessageStanza>? OnMessageReceived;
    public event Action<IqStanza>? OnIqReceived;
    public event Action<PresenceStanza>? OnPresenceReceived;

    public MockXmppServer(LoopbackTransport transport)
    {
        _transport = transport;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));
    }

    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        try
        {
            // Stage 1: Pre-Auth Stream Header
            var elem1 = await ReadElementAsync(ct); // Stream header
            await SendRawAsync($"<?xml version='1.0'?><stream:stream from='{Domain}' id='s-1' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><mechanism>PLAIN</mechanism></mechanisms></stream:features>", ct);

            // Stage 2: SASL Auth
            var authElem = await ReadElementAsync(ct);
            string mech = authElem.GetAttr("mechanism") ?? "PLAIN";

            if (mech == "PLAIN")
            {
                await SendRawAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>", ct);
            }
            else if (mech == "SCRAM-SHA-256")
            {
                // Challenge
                string saltB64 = Convert.ToBase64String("mocksalt1234"u8.ToArray());
                string challenge = $"r=mocknonce1234,s={saltB64},i=4096";
                string challengeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(challenge));
                await SendRawAsync($"<challenge xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{challengeB64}</challenge>", ct);

                var responseElem = await ReadElementAsync(ct);
                // Send success with signature
                string success = "v=" + Convert.ToBase64String("mockserversig"u8.ToArray());
                // For test simplicity in mock, if PLAIN or SCRAM, send success
                await SendRawAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>", ct);
            }

            // Stage 3: Post-Auth Stream Header
            _parser.Reset();
            _ = await ReadElementAsync(ct); // client stream header
            await SendRawAsync($"<?xml version='1.0'?><stream:stream from='{Domain}' id='s-2' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/><sm xmlns='urn:xmpp:sm:3'/></stream:features>", ct);

            // Stage 4: Resource Bind
            var bindIqElem = await ReadElementAsync(ct);
            string bindId = bindIqElem.GetAttr("id") ?? "b1";
            string resource = bindIqElem.Element("bind")?.Element("resource")?.Value ?? "NetChatx";
            await SendRawAsync($"<iq type='result' id='{bindId}'><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'><jid>alice@{Domain}/{resource}</jid></bind></iq>", ct);

            // Stage 5: Main connected loop
            await foreach (var elem in _parser.ReadAllAsync(_transport.ServerInput, ct))
            {
                if (elem.Name == "iq")
                {
                    var iq = new IqStanza(elem);
                    OnIqReceived?.Invoke(iq);
                    await HandleIqAsync(iq, ct);
                }
                else if (elem.Name == "message")
                {
                    var msg = new MessageStanza(elem);
                    OnMessageReceived?.Invoke(msg);
                }
                else if (elem.Name == "presence")
                {
                    var pres = new PresenceStanza(elem);
                    OnPresenceReceived?.Invoke(pres);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MockServer exception: {ex}");
        }
    }

    private async Task HandleIqAsync(IqStanza iq, CancellationToken ct)
    {
        // Default mock responses for ping and disco
        var ping = iq.RawElement.Element("ping", "urn:xmpp:ping");
        if (ping is not null && iq.IsGet)
        {
            var result = iq.CreateResult();
            await InjectStanzaAsync(result);
            return;
        }

        var discoInfo = iq.RawElement.Element("query", "http://jabber.org/protocol/disco#info");
        if (discoInfo is not null && iq.IsGet)
        {
            var query = new XmppElement("query", "http://jabber.org/protocol/disco#info")
                .Child(new XmppElement("identity").Attr("category", "client").Attr("type", "pc").Attr("name", "MockServer"))
                .Child(new XmppElement("feature").Attr("var", "urn:xmpp:ping"))
                .Child(new XmppElement("feature").Attr("var", "http://jabber.org/protocol/disco#info"))
                .Child(new XmppElement("feature").Attr("var", "urn:xmpp:carbons:2"))
                .Child(new XmppElement("feature").Attr("var", "urn:xmpp:mam:2"));

            var result = iq.CreateResult(query);
            await InjectStanzaAsync(result);
            return;
        }

        var roster = iq.RawElement.Element("query", "jabber:iq:roster");
        if (roster is not null && iq.IsGet)
        {
            var query = new XmppElement("query", "jabber:iq:roster")
                .Child(new XmppElement("item").Attr("jid", $"bob@{Domain}").Attr("name", "Bob").Attr("subscription", "both"));

            var result = iq.CreateResult(query);
            await InjectStanzaAsync(result);
            return;
        }

        var mam = iq.RawElement.Element("query", "urn:xmpp:mam:2");
        if (mam is not null && iq.IsSet)
        {
            string? queryId = mam.GetAttr("queryid");
            await Task.Delay(100, ct);
            var fin = new XmppElement("fin", "urn:xmpp:mam:2").Attr("complete", "true");
            if (!string.IsNullOrEmpty(queryId))
            {
                fin.Attr("queryid", queryId);
            }
            var result = iq.CreateResult(fin);
            await InjectStanzaAsync(result);
            return;
        }
    }

    public async Task InjectStanzaAsync(Stanza stanza)
    {
        string xml = stanza.ToXmlString();
        await SendRawAsync(xml, CancellationToken.None);
    }

    public async Task InjectElementAsync(XmppElement element)
    {
        string xml = element.ToXmlString();
        await SendRawAsync(xml, CancellationToken.None);
    }

    private ValueTask<XmppElement> ReadElementAsync(CancellationToken ct)
    {
        return _parser.ReadElementAsync(_transport.ServerInput, ct);
    }

    private async Task SendRawAsync(string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _transport.ServerOutput.WriteAsync(bytes, ct);
        await _transport.ServerOutput.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
    }
}
