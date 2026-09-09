using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace NetChatx.Protocol.Xeps.Omemo;

public sealed record KeyPairData(byte[] PublicKey, byte[] PrivateKey);

public static class OmemoCrypto
{
    public static KeyPairData GenerateX25519KeyPair()
    {
        var random = new SecureRandom();
        var gen = new X25519KeyPairGenerator();
        gen.Init(new X25519KeyGenerationParameters(random));
        var pair = gen.GenerateKeyPair();

        var pub = ((X25519PublicKeyParameters)pair.Public).GetEncoded();
        var priv = ((X25519PrivateKeyParameters)pair.Private).GetEncoded();
        return new KeyPairData(pub, priv);
    }

    public static byte[] CalculateSharedSecret(byte[] privateKey, byte[] publicKey)
    {
        var priv = new X25519PrivateKeyParameters(privateKey, 0);
        var pub = new X25519PublicKeyParameters(publicKey, 0);
        var secret = new byte[32];
        priv.GenerateSecret(pub, secret, 0);
        return secret;
    }

    public static byte[] EncryptAesGcm(byte[] key, byte[] iv, byte[] plaintext, byte[]? associatedData = null)
    {
        using var aesGcm = new AesGcm(key, 16);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        aesGcm.Encrypt(iv, plaintext, ciphertext, tag, associatedData);

        // Append tag to ciphertext
        byte[] result = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);
        return result;
    }

    public static byte[] DecryptAesGcm(byte[] key, byte[] iv, byte[] ciphertextWithTag, byte[]? associatedData = null)
    {
        if (ciphertextWithTag.Length < 16)
            throw new CryptographicException("Ciphertext is too short for AES-GCM tag.");

        int cipherLength = ciphertextWithTag.Length - 16;
        byte[] ciphertext = new byte[cipherLength];
        byte[] tag = new byte[16];

        Buffer.BlockCopy(ciphertextWithTag, 0, ciphertext, 0, cipherLength);
        Buffer.BlockCopy(ciphertextWithTag, cipherLength, tag, 0, 16);

        using var aesGcm = new AesGcm(key, 16);
        byte[] decrypted = new byte[cipherLength];
        aesGcm.Decrypt(iv, ciphertext, tag, decrypted, associatedData);
        return decrypted;
    }

    public static byte[] DeriveKey(byte[] ikm, byte[] salt, byte[] info, int outputLength)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, outputLength, salt, info);
    }
}
