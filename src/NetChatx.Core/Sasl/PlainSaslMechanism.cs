using System.Text;

namespace NetChatx.Core.Sasl;

public sealed class PlainSaslMechanism : ISaslMechanism
{
    public string Name => "PLAIN";

    public string? CreateInitialResponse(string username, string password)
    {
        // Format: [authzid] UTF8NUL authcid UTF8NUL passwd
        var userBytes = Encoding.UTF8.GetBytes(username);
        var passBytes = Encoding.UTF8.GetBytes(password);

        var payload = new byte[userBytes.Length + passBytes.Length + 2];
        payload[0] = 0; // null separator
        Buffer.BlockCopy(userBytes, 0, payload, 1, userBytes.Length);
        payload[userBytes.Length + 1] = 0; // null separator
        Buffer.BlockCopy(passBytes, 0, payload, userBytes.Length + 2, passBytes.Length);

        return Convert.ToBase64String(payload);
    }

    public string? HandleChallenge(string challengeBase64, string password) => null;

    public bool VerifySuccess(string? successBase64) => true;
}
