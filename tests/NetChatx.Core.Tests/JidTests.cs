using NetChatx.Core;
using Xunit;

namespace NetChatx.Core.Tests;

public class JidTests
{
    [Theory]
    [InlineData("alice@example.com", "alice", "example.com", null, true, false)]
    [InlineData("alice@example.com/mobile", "alice", "example.com", "mobile", false, true)]
    [InlineData("example.com", null, "example.com", null, true, false)]
    [InlineData("conference.example.com", null, "conference.example.com", null, true, false)]
    [InlineData("room@conference.example.com/nick", "room", "conference.example.com", "nick", false, true)]
    [InlineData("user@example.com/resource/with/slashes", "user", "example.com", "resource/with/slashes", false, true)]
    public void Parse_ValidJids_ParsesCorrectly(
        string input, string? expectedLocal, string expectedDomain, string? expectedResource, bool isBare, bool isFull)
    {
        var jid = Jid.Parse(input);

        Assert.Equal(expectedLocal, jid.LocalPart);
        Assert.Equal(expectedDomain, jid.Domain);
        Assert.Equal(expectedResource, jid.Resource);
        Assert.Equal(isBare, jid.IsBare);
        Assert.Equal(isFull, jid.IsFull);
        Assert.Equal(input, jid.ToString());
    }

    [Fact]
    public void Equals_CaseInsensitiveDomainAndLocal_CaseSensitiveResource()
    {
        var jid1 = Jid.Parse("Alice@Example.COM/Mobile");
        var jid2 = Jid.Parse("alice@example.com/Mobile");
        var jid3 = Jid.Parse("alice@example.com/mobile");

        Assert.Equal(jid1, jid2);
        Assert.NotEqual(jid1, jid3);
        Assert.True(jid1.EqualsBare(jid3));
    }

    [Fact]
    public void BareJid_ReturnsBareEquivalent()
    {
        var full = Jid.Parse("user@example.com/desktop");
        var bare = full.BareJid;

        Assert.True(bare.IsBare);
        Assert.Equal("user@example.com", bare.ToString());
    }
}
