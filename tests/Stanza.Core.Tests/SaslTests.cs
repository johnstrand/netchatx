using System.Text;
using Stanza.Core.Sasl;
using Xunit;

namespace Stanza.Core.Tests;

public class SaslTests
{
    [Fact]
    public void PlainSasl_GeneratesCorrectNullSeparatedBase64()
    {
        var sasl = new PlainSaslMechanism();
        var response = sasl.CreateInitialResponse("alice", "secret");

        Assert.NotNull(response);
        var bytes = Convert.FromBase64String(response);

        // Expected: \0alice\0secret
        var expected = Encoding.UTF8.GetBytes("\0alice\0secret");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void ScramSha256_ChallengeAndProofRoundtrip_Succeeds()
    {
        var client = new ScramSaslMechanism(isSha256: true);
        var clientFirstB64 = client.CreateInitialResponse("user", "pencil");
        Assert.NotNull(clientFirstB64);

        var clientFirst = Encoding.UTF8.GetString(Convert.FromBase64String(clientFirstB64));
        Assert.StartsWith("n,,n=user,r=", clientFirst);
        var clientNonce = clientFirst.Substring("n,,n=user,r=".Length);

        // Simulate server challenge
        var serverNonce = clientNonce + "servernonce123";
        var saltB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("random_salt"));
        var serverFirst = $"r={serverNonce},s={saltB64},i=4096";
        var serverFirstB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(serverFirst));

        var clientFinalB64 = client.HandleChallenge(serverFirstB64, "pencil");
        Assert.NotNull(clientFinalB64);

        var clientFinal = Encoding.UTF8.GetString(Convert.FromBase64String(clientFinalB64));
        Assert.StartsWith($"c=biws,r={serverNonce},p=", clientFinal);
    }
}
