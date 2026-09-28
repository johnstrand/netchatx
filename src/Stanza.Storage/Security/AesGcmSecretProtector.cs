using System.Security.Cryptography;

namespace Stanza.Storage.Security;

/// <summary>
/// AES-GCM protection for database secrets. The key must be obtained from a secure key store.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector, IDisposable
{
    private static ReadOnlySpan<byte> EnvelopeMagic => "STZ1"u8;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[] _key;
    private bool _disposed;

    public AesGcmSecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
        }

        _key = key.ToArray();
    }

    public byte[] Protect(byte[] plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plaintext);

        var result = new byte[EnvelopeMagic.Length + NonceLength + TagLength + plaintext.Length];
        EnvelopeMagic.CopyTo(result);
        var nonce = result.AsSpan(EnvelopeMagic.Length, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        var tag = result.AsSpan(EnvelopeMagic.Length + NonceLength, TagLength);
        var ciphertext = result.AsSpan(EnvelopeMagic.Length + NonceLength + TagLength);

        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, EnvelopeMagic);
        return result;
    }

    public byte[] Unprotect(byte[] protectedValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(protectedValue);

        var headerLength = EnvelopeMagic.Length + NonceLength + TagLength;
        if (protectedValue.Length < headerLength || !protectedValue.AsSpan(0, EnvelopeMagic.Length).SequenceEqual(EnvelopeMagic))
        {
            throw new CryptographicException("The protected value has an unsupported or invalid format.");
        }

        var plaintext = new byte[protectedValue.Length - headerLength];
        var nonce = protectedValue.AsSpan(EnvelopeMagic.Length, NonceLength);
        var tag = protectedValue.AsSpan(EnvelopeMagic.Length + NonceLength, TagLength);
        var ciphertext = protectedValue.AsSpan(headerLength);

        using var aes = new AesGcm(_key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, EnvelopeMagic);
        return plaintext;
    }

    internal static bool IsProtected(byte[] value)
        => value.Length >= EnvelopeMagic.Length && value.AsSpan(0, EnvelopeMagic.Length).SequenceEqual(EnvelopeMagic);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
