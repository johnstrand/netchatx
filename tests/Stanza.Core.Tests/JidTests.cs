using Stanza.Core;
using Xunit;

namespace Stanza.Core.Tests;

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceDomain_ThrowsArgumentException(string? domain)
    {
        Assert.Throws<ArgumentException>(() => new Jid("user", domain!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespaceInput_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => Jid.Parse(input!));
    }

    [Theory]
    [InlineData("@example.com")]
    [InlineData("alice@")]
    [InlineData("alice@/resource")]
    [InlineData("alice@example.com/")]
    [InlineData("/resource")]
    [InlineData("alice@domain@com")]
    [InlineData("alice@domain@com/resource")]
    public void Parse_InvalidJidFormat_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => Jid.Parse(input));
    }

    [Fact]
    public void Parse_ExceedingLengthLimits_ThrowsFormatException()
    {
        var longLocal = new string('a', 1024) + "@example.com";
        var longDomain = "user@" + new string('d', 1024);
        var longResource = "user@example.com/" + new string('r', 1024);

        Assert.Throws<FormatException>(() => Jid.Parse(longLocal));
        Assert.Throws<FormatException>(() => Jid.Parse(longDomain));
        Assert.Throws<FormatException>(() => Jid.Parse(longResource));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.com")]
    [InlineData("alice@")]
    [InlineData("alice@/resource")]
    [InlineData("alice@example.com/")]
    [InlineData("/resource")]
    [InlineData("alice@domain@com")]
    [InlineData("alice@domain@com/resource")]
    public void TryParse_InvalidOrWhitespaceInput_ReturnsFalseAndNullResult(string? input)
    {
        var success = Jid.TryParse(input, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_ExceedingLengthLimits_ReturnsFalseAndNullResult()
    {
        var longLocal = new string('a', 1024) + "@example.com";
        var longDomain = "user@" + new string('d', 1024);
        var longResource = "user@example.com/" + new string('r', 1024);

        Assert.False(Jid.TryParse(longLocal, out var r1));
        Assert.Null(r1);

        Assert.False(Jid.TryParse(longDomain, out var r2));
        Assert.Null(r2);

        Assert.False(Jid.TryParse(longResource, out var r3));
        Assert.Null(r3);
    }

    [Theory]
    [InlineData("alice@example.com", "alice", "example.com", null)]
    [InlineData("alice@example.com/mobile", "alice", "example.com", "mobile")]
    [InlineData("example.com", null, "example.com", null)]
    [InlineData("domain.com/user@resource", null, "domain.com", "user@resource")]
    public void TryParse_ValidInput_ReturnsTrueAndJid(
        string input, string? expectedLocal, string expectedDomain, string? expectedResource)
    {
        var success = Jid.TryParse(input, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(expectedLocal, result.LocalPart);
        Assert.Equal(expectedDomain, result.Domain);
        Assert.Equal(expectedResource, result.Resource);
    }

    [Fact]
    public void WithResource_UpdatesResourceCorrectly()
    {
        var bare = Jid.Parse("user@example.com");
        var withRes = bare.WithResource("desktop");

        Assert.Equal("desktop", withRes.Resource);
        Assert.Equal("user@example.com/desktop", withRes.ToString());

        var clearedRes = withRes.WithResource(null);
        Assert.Null(clearedRes.Resource);
        Assert.Equal("user@example.com", clearedRes.ToString());
    }

    [Fact]
    public void EqualityAndComparison_HandlesNullAndOperators()
    {
        var jid = Jid.Parse("alice@example.com");

        Assert.False(jid.Equals((Jid?)null));
        Assert.False(jid.Equals((object?)null));
        Assert.False(jid.EqualsBare(null));

        Jid? nullJid = null;
        Assert.True(nullJid == null);
        Assert.True(jid != null);
        Assert.False(jid == null);

        var same = Jid.Parse("alice@example.com");
        Assert.True(jid == same);
        Assert.False(jid != same);

        var str = jid;
        Assert.Equal("alice@example.com", str);

        var explicitJid = (Jid)"alice@example.com";
        Assert.Equal(jid, explicitJid);

        Assert.True(jid.CompareTo(null) > 0);
        Assert.True(Jid.Parse("a@domain.com").CompareTo(Jid.Parse("b@domain.com")) < 0);
    }
}
