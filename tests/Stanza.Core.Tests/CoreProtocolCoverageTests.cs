using System;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Stanza.Core.Sasl;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Xunit;

namespace Stanza.Core.Tests;

public class CoreProtocolCoverageTests
{
    [Fact]
    public void ScramSha1_ChallengeProofAndVerifySuccess_Succeeds()
    {
        var client = new ScramSaslMechanism(isSha256: false);
        Assert.Equal("SCRAM-SHA-1", client.Name);

        var clientFirstB64 = client.CreateInitialResponse("user", "pencil");
        Assert.NotNull(clientFirstB64);

        var clientFirst = Encoding.UTF8.GetString(Convert.FromBase64String(clientFirstB64));
        var clientNonce = clientFirst.Substring("n,,n=user,r=".Length);

        var serverNonce = clientNonce + "servernonce123";
        var saltB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("random_salt_sha1"));
        var serverFirst = $"r={serverNonce},s={saltB64},i=4096";
        var serverFirstB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(serverFirst));

        var clientFinalB64 = client.HandleChallenge(serverFirstB64, "pencil");
        Assert.NotNull(clientFinalB64);

        // Verify failure cases in VerifySuccess
        Assert.False(client.VerifySuccess(null));
        Assert.False(client.VerifySuccess(""));
        var fakeSigB64 = Convert.ToBase64String(new byte[20]);
        var fakeSuccess = $"v={fakeSigB64}";
        Assert.False(client.VerifySuccess(Convert.ToBase64String(Encoding.UTF8.GetBytes(fakeSuccess))));
    }

    [Fact]
    public void ScramSasl_HandleChallenge_ValidatesErrorsCorrectly()
    {
        var client = new ScramSaslMechanism(isSha256: true);

        // Cannot handle challenge before CreateInitialResponse
        Assert.Throws<InvalidOperationException>(() => client.HandleChallenge("c3R1ZmY=", "password"));

        client.CreateInitialResponse("user", "password");

        // Missing attributes in server message
        var malformed = Convert.ToBase64String(Encoding.UTF8.GetBytes("invalid_format"));
        Assert.Throws<FormatException>(() => client.HandleChallenge(malformed, "password"));

        // Nonce mismatch
        var badNonceMsg = $"r=different_nonce,s={Convert.ToBase64String("salt"u8.ToArray())},i=4096";
        var badNonceB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(badNonceMsg));
        Assert.Throws<CryptographicException>(() => client.HandleChallenge(badNonceB64, "password"));
    }

    [Fact]
    public void IqStanza_QueryProperty_GetAndSet_WorksCorrectly()
    {
        var iq = IqStanza.CreateGet(Jid.Parse("server.com"));
        Assert.Null(iq.Query);

        var query1 = new XmppElement("query", "jabber:iq:roster");
        iq.Query = query1;
        Assert.NotNull(iq.Query);
        Assert.Equal("jabber:iq:roster", iq.Query.Namespace);

        // Replace existing query
        var query2 = new XmppElement("query", "urn:xmpp:ping");
        iq.Query = query2;
        Assert.NotNull(iq.Query);
        Assert.Equal("urn:xmpp:ping", iq.Query.Namespace);

        // Clear query
        iq.Query = null;
        Assert.Null(iq.Query);
    }

    [Fact]
    public async Task LoopbackTransport_ConnectUpgradeAndClose_Succeeds()
    {
        await using var transport = new LoopbackTransport();
        Assert.NotNull(transport.Input);
        Assert.NotNull(transport.Output);
        Assert.NotNull(transport.ServerInput);
        Assert.NotNull(transport.ServerOutput);
        Assert.False(transport.IsSecure);

        await transport.ConnectAsync("localhost", 5222);
        await transport.UpgradeToTlsAsync("localhost");
        Assert.True(transport.IsSecure);

        await transport.CloseAsync();
    }

    [Fact]
    public async Task XmppStreamParser_CDataCommentsAndClosingStream_ParsedCorrectly()
    {
        var parser = new XmppStreamParser();

        // 1. Initial header
        var header = parser.ParseChunk("<stream:stream to='example.com' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").Single();
        Assert.Equal("stream", header.Name);
        Assert.Equal("stream", header.Prefix);

        // 2. Message with CDATA and comments
        var xml = "<message to='bob@example.com'><body><![CDATA[Hello <xml> in cdata]]></body><!-- comment here --><extra/></message>";
        var elements = parser.ParseChunk(xml).ToList();
        Assert.Single(elements);
        var msg = elements[0];
        Assert.Equal("message", msg.Name);
        Assert.Equal("Hello <xml> in cdata", msg.Element("body")?.Value);
        Assert.NotNull(msg.Element("extra"));

        // 3. Closing stream: </stream:stream>
        var closeStream = parser.ParseChunk("</stream:stream>").Single();
        Assert.Equal("true", closeStream.GetAttr("closed"));

        // 4. Reset parser
        parser.Reset();

        // 5. Short stream header: <stream ...>
        var shortHeader = parser.ParseChunk("<stream xmlns='jabber:client'>").Single();
        Assert.Equal("stream", shortHeader.Name);

        // 6. Closing stream: </stream>
        var closeShort = parser.ParseChunk("</stream>").Single();
        Assert.Equal("true", closeShort.GetAttr("closed"));

        // 7. PipeReader async streaming with ReadAllAsync
        parser.Reset();
        var pipe = new Pipe();
        var data = Encoding.UTF8.GetBytes("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><message><body>Hi</body></message></stream:stream>");
        await pipe.Writer.WriteAsync(data);
        await pipe.Writer.CompleteAsync();

        var streamedElements = new List<XmppElement>();
        await foreach (var elem in parser.ReadAllAsync(pipe.Reader))
        {
            streamedElements.Add(elem);
        }

        Assert.Equal(3, streamedElements.Count); // stream header, message, stream:stream (closed)
        Assert.Equal("stream", streamedElements[0].Name);
        Assert.Equal("message", streamedElements[1].Name);
        Assert.Equal("stream:stream", streamedElements[2].Name);
        Assert.Equal("true", streamedElements[2].GetAttr("closed"));
    }

    [Fact]
    public async Task XmppStreamParser_ReadElementAsync_ThrowsEndOfStreamWhenCompleted()
    {
        var parser = new XmppStreamParser();
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<System.IO.EndOfStreamException>(async () =>
        {
            await parser.ReadElementAsync(pipe.Reader);
        });
    }
}

