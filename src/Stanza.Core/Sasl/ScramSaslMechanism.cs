using System.Security.Cryptography;
using System.Text;

namespace Stanza.Core.Sasl;

public sealed class ScramSaslMechanism : ISaslMechanism
{
    private readonly HashAlgorithmName _hashAlgorithm;
    private readonly int _hashLength;

    public string Name { get; }

    private string? _clientNonce;
    private string? _clientFirstMessageBare;
    private byte[]? _expectedServerSignature;

    public ScramSaslMechanism(bool isSha256 = true)
    {
        if (isSha256)
        {
            Name = "SCRAM-SHA-256";
            _hashAlgorithm = HashAlgorithmName.SHA256;
            _hashLength = 32;
        }
        else
        {
            Name = "SCRAM-SHA-1";
            _hashAlgorithm = HashAlgorithmName.SHA1;
            _hashLength = 20;
        }
    }

    public string? CreateInitialResponse(string username, string password)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(18);
        _clientNonce = Convert.ToBase64String(nonceBytes);

        var normalizedUser = username.Normalize(NormalizationForm.FormKC);
        var escapedUser = normalizedUser.Replace("=", "=3D").Replace(",", "=2C");
        _clientFirstMessageBare = $"n={escapedUser},r={_clientNonce}";

        var clientFirstMessage = $"n,,{_clientFirstMessageBare}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirstMessage));
    }

    public string? HandleChallenge(string challengeBase64, string password)
    {
        if (_clientNonce is null || _clientFirstMessageBare is null)
            throw new InvalidOperationException("Initial response was not generated.");

        var challengeBytes = Convert.FromBase64String(challengeBase64);
        var serverFirstMessage = Encoding.UTF8.GetString(challengeBytes);

        var parts = ParseAttributes(serverFirstMessage);
        if (!parts.TryGetValue("r", out string? fullNonce) ||
            !parts.TryGetValue("s", out string? saltB64) ||
            !parts.TryGetValue("i", out string? iterStr) ||
            !int.TryParse(iterStr, out int iterations))
        {
            throw new FormatException("Malformed SCRAM server first message.");
        }

        if (!fullNonce.StartsWith(_clientNonce, StringComparison.Ordinal))
        {
            throw new CryptographicException("Server nonce does not match client nonce.");
        }

        var salt = Convert.FromBase64String(saltB64);
        var normalizedPassword = password.Normalize(NormalizationForm.FormKC);
        var passwordBytes = Encoding.UTF8.GetBytes(normalizedPassword);

        // SaltedPassword = Hi(Normalize(password), salt, i)
        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, _hashAlgorithm, _hashLength);

        // ClientKey = HMAC(SaltedPassword, "Client Key")
        var clientKey = ComputeHmac(saltedPassword, "Client Key"u8);

        // StoredKey = H(ClientKey)
        var storedKey = ComputeHash(clientKey);

        // AuthMessage = client-first-message-bare + "," + server-first-message + "," + client-final-message-without-proof
        var clientFinalWithoutProof = $"c=biws,r={fullNonce}";
        var authMessage = $"{_clientFirstMessageBare},{serverFirstMessage},{clientFinalWithoutProof}";
        var authMessageBytes = Encoding.UTF8.GetBytes(authMessage);

        // ClientSignature = HMAC(StoredKey, AuthMessage)
        var clientSignature = ComputeHmac(storedKey, authMessageBytes);

        // ClientProof = ClientKey XOR ClientSignature
        var clientProof = new byte[clientKey.Length];
        for (int i = 0; i < clientKey.Length; i++)
        {
            clientProof[i] = (byte)(clientKey[i] ^ clientSignature[i]);
        }

        // ServerKey = HMAC(SaltedPassword, "Server Key")
        var serverKey = ComputeHmac(saltedPassword, "Server Key"u8);

        // ServerSignature = HMAC(ServerKey, AuthMessage)
        _expectedServerSignature = ComputeHmac(serverKey, authMessageBytes);

        var clientFinalMessage = $"{clientFinalWithoutProof},p={Convert.ToBase64String(clientProof)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFinalMessage));
    }

    public bool VerifySuccess(string? successBase64)
    {
        if (string.IsNullOrEmpty(successBase64) || _expectedServerSignature is null)
            return false;

        var successBytes = Convert.FromBase64String(successBase64);
        var successStr = Encoding.UTF8.GetString(successBytes);

        var parts = ParseAttributes(successStr);
        if (!parts.TryGetValue("v", out string? serverSigB64))
            return false;

        var serverSig = Convert.FromBase64String(serverSigB64);
        return CryptographicOperations.FixedTimeEquals(serverSig, _expectedServerSignature);
    }

    private byte[] ComputeHmac(byte[] key, ReadOnlySpan<byte> data)
    {
        if (_hashAlgorithm == HashAlgorithmName.SHA256)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data.ToArray());
        }
        else
        {
            using var hmac = new HMACSHA1(key);
            return hmac.ComputeHash(data.ToArray());
        }
    }

    private byte[] ComputeHash(byte[] data)
    {
        if (_hashAlgorithm == HashAlgorithmName.SHA256)
        {
            return SHA256.HashData(data);
        }
        else
        {
            return SHA1.HashData(data);
        }
    }

    private static Dictionary<string, string> ParseAttributes(string message)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = message.Split(',');
        foreach (var entry in entries)
        {
            var eq = entry.IndexOf('=');
            if (eq > 0)
            {
                dict[entry[..eq]] = entry[(eq + 1)..];
            }
        }
        return dict;
    }
}
