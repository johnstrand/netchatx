namespace NetChatx.Core.Sasl;

public interface ISaslMechanism
{
    string Name { get; }

    /// <summary>
    /// Generates the initial SASL message payload (base64 or text) to send in &lt;auth mechanism='...'&gt;.
    /// </summary>
    string? CreateInitialResponse(string username, string password);

    /// <summary>
    /// Evaluates a server &lt;challenge&gt; and computes the response for &lt;response&gt;.
    /// </summary>
    string? HandleChallenge(string challengeBase64, string password);

    /// <summary>
    /// Verifies the server &lt;success&gt; message payload if required by the mechanism.
    /// </summary>
    bool VerifySuccess(string? successBase64);
}
