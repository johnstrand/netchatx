using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class PresenceStanzaTests
{
    [Fact]
    public void Available_WithDefaultParameters_CreatesAvailablePresence()
    {
        var stanza = PresenceStanza.Available();

        Assert.Null(stanza.Type);
        Assert.Null(stanza.Show);
        Assert.Null(stanza.Status);
        Assert.Equal(0, stanza.Priority);
        Assert.True(stanza.IsAvailable);
    }

    [Fact]
    public void Available_WithCustomParameters_SetsPropertiesCorrectly()
    {
        var stanza = PresenceStanza.Available(show: PresenceStanza.ShowAway, status: "In a meeting", priority: 10);

        Assert.Null(stanza.Type);
        Assert.Equal(PresenceStanza.ShowAway, stanza.Show);
        Assert.Equal("In a meeting", stanza.Status);
        Assert.Equal(10, stanza.Priority);
        Assert.True(stanza.IsAvailable);
    }

    [Fact]
    public void Unavailable_WithDefaultParameters_CreatesUnavailablePresence()
    {
        var stanza = PresenceStanza.Unavailable();

        Assert.Equal(PresenceStanza.TypeUnavailable, stanza.Type);
        Assert.Null(stanza.Status);
        Assert.False(stanza.IsAvailable);
    }

    [Fact]
    public void Unavailable_WithCustomStatus_SetsStatusCorrectly()
    {
        var stanza = PresenceStanza.Unavailable(status: "Out of office");

        Assert.Equal(PresenceStanza.TypeUnavailable, stanza.Type);
        Assert.Equal("Out of office", stanza.Status);
        Assert.False(stanza.IsAvailable);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("chat", true)]
    [InlineData("subscribe", true)]
    [InlineData("unavailable", false)]
    [InlineData("UNAVAILABLE", false)]
    public void IsAvailable_ReturnsExpectedResultBasedOnType(string? type, bool expectedIsAvailable)
    {
        var stanza = new PresenceStanza(type: type);
        Assert.Equal(expectedIsAvailable, stanza.IsAvailable);
    }

    [Fact]
    public void ShowProperty_AddUpdateAndRemove_MutatesXmlCorrectly()
    {
        var stanza = PresenceStanza.Available();
        Assert.Null(stanza.Show);

        // Add
        stanza.Show = PresenceStanza.ShowDnd;
        Assert.Equal(PresenceStanza.ShowDnd, stanza.Show);

        // Update
        stanza.Show = PresenceStanza.ShowXa;
        Assert.Equal(PresenceStanza.ShowXa, stanza.Show);

        // Remove
        stanza.Show = null;
        Assert.Null(stanza.Show);
        Assert.Null(stanza.RawElement.Element("show"));
    }

    [Fact]
    public void StatusProperty_AddUpdateAndRemove_MutatesXmlCorrectly()
    {
        var stanza = PresenceStanza.Available();
        Assert.Null(stanza.Status);

        // Add
        stanza.Status = "Coding";
        Assert.Equal("Coding", stanza.Status);

        // Update
        stanza.Status = "Testing";
        Assert.Equal("Testing", stanza.Status);

        // Remove
        stanza.Status = null;
        Assert.Null(stanza.Status);
        Assert.Null(stanza.RawElement.Element("status"));
    }

    [Fact]
    public void PriorityProperty_AddUpdateAndRemove_MutatesXmlCorrectly()
    {
        var stanza = PresenceStanza.Available();
        Assert.Equal(0, stanza.Priority);

        // Update
        stanza.Priority = 5;
        Assert.Equal(5, stanza.Priority);

        // Remove
        stanza.Priority = null;
        Assert.Null(stanza.Priority);
        Assert.Null(stanza.RawElement.Element("priority"));
    }

    [Fact]
    public void PriorityProperty_NonNumericXmlValue_ReturnsNull()
    {
        var element = new XmppElement("presence")
            .Child(new XmppElement("priority") { Value = "not-a-number" });

        var stanza = new PresenceStanza(element);

        Assert.Null(stanza.Priority);
    }
}
