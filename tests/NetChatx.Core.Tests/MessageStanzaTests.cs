using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class MessageStanzaTests
{
    [Fact]
    public void Body_WhenAbsent_ReturnsNull()
    {
        var stanza = new MessageStanza();
        Assert.Null(stanza.Body);
    }

    [Fact]
    public void Body_SetNonNullValueWhenAbsent_CreatesBodyElement()
    {
        var stanza = new MessageStanza();
        stanza.Body = "Hello World";

        Assert.Equal("Hello World", stanza.Body);
        var bodyElem = stanza.RawElement.Element("body");
        Assert.NotNull(bodyElem);
        Assert.Equal("Hello World", bodyElem.Value);
    }

    [Fact]
    public void Body_UpdateExistingValue_UpdatesBodyElement()
    {
        var stanza = new MessageStanza(body: "Initial");
        Assert.Equal("Initial", stanza.Body);

        stanza.Body = "Updated";
        Assert.Equal("Updated", stanza.Body);
        var bodyElem = stanza.RawElement.Element("body");
        Assert.NotNull(bodyElem);
        Assert.Equal("Updated", bodyElem.Value);
    }

    [Fact]
    public void Body_SetNullWhenPresent_RemovesBodyElement()
    {
        var stanza = new MessageStanza(body: "Hello");
        Assert.NotNull(stanza.RawElement.Element("body"));

        stanza.Body = null;

        Assert.Null(stanza.Body);
        Assert.Null(stanza.RawElement.Element("body"));
    }

    [Fact]
    public void Body_SetNullWhenAbsent_DoesNotThrowOrAddElement()
    {
        var stanza = new MessageStanza();

        stanza.Body = null;

        Assert.Null(stanza.Body);
        Assert.Null(stanza.RawElement.Element("body"));
    }

    [Fact]
    public void Subject_WhenAbsent_ReturnsNull()
    {
        var stanza = new MessageStanza();
        Assert.Null(stanza.Subject);
    }

    [Fact]
    public void Subject_SetNonNullValueWhenAbsent_CreatesSubjectElement()
    {
        var stanza = new MessageStanza();
        stanza.Subject = "Greeting";

        Assert.Equal("Greeting", stanza.Subject);
        var subjectElem = stanza.RawElement.Element("subject");
        Assert.NotNull(subjectElem);
        Assert.Equal("Greeting", subjectElem.Value);
    }

    [Fact]
    public void Subject_UpdateExistingValue_UpdatesSubjectElement()
    {
        var stanza = new MessageStanza();
        stanza.Subject = "Original Subject";

        stanza.Subject = "New Subject";
        Assert.Equal("New Subject", stanza.Subject);
        var subjectElem = stanza.RawElement.Element("subject");
        Assert.NotNull(subjectElem);
        Assert.Equal("New Subject", subjectElem.Value);
    }

    [Fact]
    public void Subject_SetNullWhenPresent_RemovesSubjectElement()
    {
        var stanza = new MessageStanza();
        stanza.Subject = "Topic";
        Assert.NotNull(stanza.RawElement.Element("subject"));

        stanza.Subject = null;

        Assert.Null(stanza.Subject);
        Assert.Null(stanza.RawElement.Element("subject"));
    }

    [Fact]
    public void Subject_SetNullWhenAbsent_DoesNotThrowOrAddElement()
    {
        var stanza = new MessageStanza();

        stanza.Subject = null;

        Assert.Null(stanza.Subject);
        Assert.Null(stanza.RawElement.Element("subject"));
    }

    [Fact]
    public void Thread_WhenAbsent_ReturnsNull()
    {
        var stanza = new MessageStanza();
        Assert.Null(stanza.Thread);
    }

    [Fact]
    public void Thread_SetNonNullValueWhenAbsent_CreatesThreadElement()
    {
        var stanza = new MessageStanza();
        stanza.Thread = "thread-12345";

        Assert.Equal("thread-12345", stanza.Thread);
        var threadElem = stanza.RawElement.Element("thread");
        Assert.NotNull(threadElem);
        Assert.Equal("thread-12345", threadElem.Value);
    }

    [Fact]
    public void Thread_UpdateExistingValue_UpdatesThreadElement()
    {
        var stanza = new MessageStanza();
        stanza.Thread = "thread-1";

        stanza.Thread = "thread-2";
        Assert.Equal("thread-2", stanza.Thread);
        var threadElem = stanza.RawElement.Element("thread");
        Assert.NotNull(threadElem);
        Assert.Equal("thread-2", threadElem.Value);
    }

    [Fact]
    public void Thread_SetNullWhenPresent_RemovesThreadElement()
    {
        var stanza = new MessageStanza();
        stanza.Thread = "thread-12345";
        Assert.NotNull(stanza.RawElement.Element("thread"));

        stanza.Thread = null;

        Assert.Null(stanza.Thread);
        Assert.Null(stanza.RawElement.Element("thread"));
    }

    [Fact]
    public void Thread_SetNullWhenAbsent_DoesNotThrowOrAddElement()
    {
        var stanza = new MessageStanza();

        stanza.Thread = null;

        Assert.Null(stanza.Thread);
        Assert.Null(stanza.RawElement.Element("thread"));
    }

    [Fact]
    public void Constructor_FromRawElement_ParsesPropertiesCorrectly()
    {
        var elem = XmppElement.Parse(
            "<message to='alice@example.com' from='bob@example.com' type='chat' id='msg-1'>" +
            "<subject>Hello</subject>" +
            "<body>World</body>" +
            "<thread>t-1</thread>" +
            "</message>");

        var stanza = new MessageStanza(elem);

        Assert.Equal("alice@example.com", stanza.To?.ToString());
        Assert.Equal("bob@example.com", stanza.From?.ToString());
        Assert.Equal("chat", stanza.Type);
        Assert.Equal("msg-1", stanza.Id);
        Assert.Equal("Hello", stanza.Subject);
        Assert.Equal("World", stanza.Body);
        Assert.Equal("t-1", stanza.Thread);
    }

    [Fact]
    public void Constructor_WithParameters_SetsPropertiesCorrectly()
    {
        var to = Jid.Parse("alice@example.com");
        var from = Jid.Parse("bob@example.com");

        var stanza = new MessageStanza(to, "Test body", MessageStanza.TypeChat, from, "msg-2");

        Assert.Equal(to, stanza.To);
        Assert.Equal(from, stanza.From);
        Assert.Equal(MessageStanza.TypeChat, stanza.Type);
        Assert.Equal("msg-2", stanza.Id);
        Assert.Equal("Test body", stanza.Body);
    }

    [Fact]
    public void CreateChat_CreatesValidChatMessage()
    {
        var to = Jid.Parse("alice@example.com");
        var from = Jid.Parse("bob@example.com");

        var stanza = MessageStanza.CreateChat(to, "Hello Alice", from);

        Assert.Equal(to, stanza.To);
        Assert.Equal(from, stanza.From);
        Assert.Equal("chat", stanza.Type);
        Assert.Equal("Hello Alice", stanza.Body);
    }

    [Fact]
    public void CreateGroupChat_CreatesValidGroupChatMessage()
    {
        var toRoom = Jid.Parse("room@conference.example.com");
        var from = Jid.Parse("bob@example.com/mobile");

        var stanza = MessageStanza.CreateGroupChat(toRoom, "Hello Room", from);

        Assert.Equal(toRoom, stanza.To);
        Assert.Equal(from, stanza.From);
        Assert.Equal("groupchat", stanza.Type);
        Assert.Equal("Hello Room", stanza.Body);
    }

    [Fact]
    public void CreateGroupChat_WithoutFrom_SetsPropertiesCorrectly()
    {
        var roomJid = Jid.Parse("room@conference.example.com");
        string bodyText = "Hello room!";

        var stanza = MessageStanza.CreateGroupChat(roomJid, bodyText);

        Assert.Equal(roomJid, stanza.To);
        Assert.Equal(bodyText, stanza.Body);
        Assert.Equal(MessageStanza.TypeGroupChat, stanza.Type);
        Assert.Null(stanza.From);
    }

    [Fact]
    public void CreateGroupChat_WithFrom_SetsPropertiesCorrectly()
    {
        var roomJid = Jid.Parse("room@conference.example.com");
        var senderJid = Jid.Parse("user@example.com/res");
        string bodyText = "Hello room with from!";

        var stanza = MessageStanza.CreateGroupChat(roomJid, bodyText, senderJid);

        Assert.Equal(roomJid, stanza.To);
        Assert.Equal(bodyText, stanza.Body);
        Assert.Equal(MessageStanza.TypeGroupChat, stanza.Type);
        Assert.Equal(senderJid, stanza.From);
    }

    [Fact]
    public void CreateChat_WithoutFrom_SetsPropertiesCorrectly()
    {
        var recipientJid = Jid.Parse("alice@example.com");
        string bodyText = "Hello Alice!";

        var stanza = MessageStanza.CreateChat(recipientJid, bodyText);

        Assert.Equal(recipientJid, stanza.To);
        Assert.Equal(bodyText, stanza.Body);
        Assert.Equal(MessageStanza.TypeChat, stanza.Type);
        Assert.Null(stanza.From);
    }

    [Fact]
    public void CreateChat_WithFrom_SetsPropertiesCorrectly()
    {
        var recipientJid = Jid.Parse("alice@example.com");
        var senderJid = Jid.Parse("bob@example.com/mobile");
        string bodyText = "Hello Alice from Bob!";

        var stanza = MessageStanza.CreateChat(recipientJid, bodyText, senderJid);

        Assert.Equal(recipientJid, stanza.To);
        Assert.Equal(bodyText, stanza.Body);
        Assert.Equal(MessageStanza.TypeChat, stanza.Type);
        Assert.Equal(senderJid, stanza.From);
    }
}
