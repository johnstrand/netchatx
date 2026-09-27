using System.Text;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;

namespace Stanza.MockServer;

public sealed class MockXmppServer : IAsyncDisposable
{
    private readonly LoopbackTransport _transport;
    private readonly XmppStreamParser _parser = new();
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public string Domain { get; set; } = "mock.example.com";
    public string ExpectedPassword { get; set; } = "password123";

    private readonly HashSet<string> _mockBlockedJids = new(StringComparer.OrdinalIgnoreCase);

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
            await SendRawAsync($"<?xml version='1.0'?><stream:stream from='{Domain}' id='s-1' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><mechanism>PLAIN</mechanism></mechanisms><register xmlns='http://jabber.org/features/iq-register'/></stream:features>", ct);

            // Stage 2: Pre-auth loop (handles IQ for in-band registration or auth element for SASL)
            XmppElement? authElem = null;
            while (true)
            {
                var nextElem = await ReadElementAsync(ct);
                if (nextElem.Name == "auth")
                {
                    authElem = nextElem;
                    break;
                }

                if (nextElem.Name == "iq")
                {
                    var iq = new IqStanza(nextElem);
                    OnIqReceived?.Invoke(iq);
                    var regQuery = iq.RawElement.Element("query", "jabber:iq:register");
                    if (regQuery is not null)
                    {
                        if (iq.IsGet)
                        {
                            await SendRawAsync($"<iq type='result' id='{iq.Id}' from='{Domain}'><query xmlns='jabber:iq:register'><instructions>Choose a username and password.</instructions><username/><password/><email/></query></iq>", ct);
                        }
                        else if (iq.IsSet)
                        {
                            var user = regQuery.Element("username")?.Value;
                            if (user == "conflict_user")
                            {
                                await SendRawAsync($"<iq type='error' id='{iq.Id}' from='{Domain}'><error type='cancel'><conflict xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/><text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>Username already exists</text></error></iq>", ct);
                            }
                            else if (user == "captcha_user")
                            {
                                var xElem = regQuery.Element("x", "jabber:x:data");
                                var ans = xElem?.Elements("field").FirstOrDefault(f => f.GetAttr("var") == "answers")?.Element("value")?.Value;
                                if (ans == "8")
                                {
                                    await SendRawAsync($"<iq type='result' id='{iq.Id}' from='{Domain}'/>", ct);
                                }
                                else
                                {
                                    await SendRawAsync($"<iq type='error' id='{iq.Id}' from='{Domain}'><error type='modify' code='406'><not-acceptable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/><text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>CAPTCHA challenge required</text><captcha xmlns='urn:xmpp:captcha'><x xmlns='jabber:x:data' type='form'><field var='FORM_TYPE' type='hidden'><value>urn:xmpp:captcha</value></field><field var='challenge' type='hidden'><value>chal-123</value></field><field var='answers' type='text-single' label='What is 5 + 3?'><required/></field></x></captcha></error></iq>", ct);
                                }
                            }
                            else
                            {
                                await SendRawAsync($"<iq type='result' id='{iq.Id}' from='{Domain}'/>", ct);
                            }
                        }
                    }
                    else
                    {
                        await SendRawAsync($"<iq type='error' id='{iq.Id}'><error type='cancel'><service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></iq>", ct);
                    }
                }
            }
            var mech = authElem.GetAttr("mechanism") ?? "PLAIN";

            if (mech == "PLAIN")
            {
                await SendRawAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>", ct);
            }
            else if (mech == "SCRAM-SHA-256")
            {
                // Challenge
                var saltB64 = Convert.ToBase64String("mocksalt1234"u8.ToArray());
                var challenge = $"r=mocknonce1234,s={saltB64},i=4096";
                var challengeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(challenge));
                await SendRawAsync($"<challenge xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{challengeB64}</challenge>", ct);

                var responseElem = await ReadElementAsync(ct);
                // Send success with signature
                var success = "v=" + Convert.ToBase64String("mockserversig"u8.ToArray());
                // For test simplicity in mock, if PLAIN or SCRAM, send success
                await SendRawAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>", ct);
            }

            // Stage 3: Post-Auth Stream Header
            _parser.Reset();
            _ = await ReadElementAsync(ct); // client stream header
            await SendRawAsync($"<?xml version='1.0'?><stream:stream from='{Domain}' id='s-2' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/><sm xmlns='urn:xmpp:sm:3'/></stream:features>", ct);

            // Stage 4: Resource Bind
            var bindIqElem = await ReadElementAsync(ct);
            var bindId = bindIqElem.GetAttr("id") ?? "b1";
            var resource = bindIqElem.Element("bind")?.Element("resource")?.Value ?? "Stanza";
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
                .Child(new XmppElement("feature").Attr("var", "urn:xmpp:mam:2"))
                .Child(new XmppElement("feature").Attr("var", "urn:xmpp:blocking"));

            var result = iq.CreateResult(query);
            await InjectStanzaAsync(result);
            return;
        }

        var blocklist = iq.RawElement.Element("blocklist", "urn:xmpp:blocking");
        if (blocklist is not null && iq.IsGet)
        {
            var resBlocklist = new XmppElement("blocklist", "urn:xmpp:blocking");
            foreach (var j in _mockBlockedJids)
            {
                resBlocklist.Child(new XmppElement("item").Attr("jid", j));
            }
            var result = iq.CreateResult(resBlocklist);
            await InjectStanzaAsync(result);
            return;
        }

        var block = iq.RawElement.Element("block", "urn:xmpp:blocking");
        if (block is not null && iq.IsSet)
        {
            foreach (var item in block.Elements("item"))
            {
                var j = item.GetAttr("jid");
                if (!string.IsNullOrEmpty(j)) _mockBlockedJids.Add(j);
            }
            var result = iq.CreateResult();
            await InjectStanzaAsync(result);
            return;
        }

        var unblock = iq.RawElement.Element("unblock", "urn:xmpp:blocking");
        if (unblock is not null && iq.IsSet)
        {
            var items = unblock.Elements("item").ToList();
            if (items.Count > 0)
            {
                foreach (var item in items)
                {
                    var j = item.GetAttr("jid");
                    if (!string.IsNullOrEmpty(j)) _mockBlockedJids.Remove(j);
                }
            }
            else
            {
                _mockBlockedJids.Clear();
            }
            var result = iq.CreateResult();
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

        var carbonsEnable = iq.RawElement.Element("enable", "urn:xmpp:carbons:2") ?? iq.RawElement.Element("disable", "urn:xmpp:carbons:2");
        if (carbonsEnable is not null && iq.IsSet)
        {
            var result = iq.CreateResult();
            await InjectStanzaAsync(result);
            return;
        }

        var mam = iq.RawElement.Element("query", "urn:xmpp:mam:2");
        if (mam is not null && iq.IsSet)
        {
            var queryId = mam.GetAttr("queryid");
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

        var regQuery = iq.RawElement.Element("query", "jabber:iq:register");
        if (regQuery is not null)
        {
            if (iq.IsGet)
            {
                var query = new XmppElement("query", "jabber:iq:register")
                    .Child(new XmppElement("registered"))
                    .Child(new XmppElement("username") { Value = "alice" });
                var result = iq.CreateResult(query);
                await InjectStanzaAsync(result);
                return;
            }
            if (iq.IsSet)
            {
                var result = iq.CreateResult();
                await InjectStanzaAsync(result);
                return;
            }
        }
    }

    public async Task InjectStanzaAsync(XmppStanza stanza)
    {
        var xml = stanza.ToXmlString();
        await SendRawAsync(xml, CancellationToken.None);
    }

    public async Task InjectElementAsync(XmppElement element)
    {
        var xml = element.ToXmlString();
        await SendRawAsync(xml, CancellationToken.None);
    }

    private ValueTask<XmppElement> ReadElementAsync(CancellationToken ct)
    {
        return _parser.ReadElementAsync(_transport.ServerInput, ct);
    }

    private async Task SendRawAsync(string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
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
