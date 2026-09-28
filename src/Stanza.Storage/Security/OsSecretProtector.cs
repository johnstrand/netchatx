using System.Security.Cryptography;
using GnomeStack.Os.Secrets;

namespace Stanza.Storage.Security;

/// <summary>
/// Protects database values with an AES key held in Windows Credential Manager, macOS Keychain,
/// or Linux Secret Service. It fails closed when the platform vault is unavailable.
/// </summary>
public sealed class OsSecretProtector : ISecretProtector
{
    private const string VaultService = "Stanza.DatabaseEncryption";
    private const string VaultAccount = "database-key-v1";
    private static readonly object VaultLock = new();

    public byte[] Protect(byte[] plaintext)
    {
        var key = GetKey(createIfMissing: true);
        try
        {
            using var protector = new AesGcmSecretProtector(key);
            return protector.Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Unprotect(byte[] protectedValue)
    {
        var key = GetKey(createIfMissing: false);
        try
        {
            using var protector = new AesGcmSecretProtector(key);
            return protector.Unprotect(protectedValue);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] GetKey(bool createIfMissing)
    {
        lock (VaultLock)
        {
            try
            {
                var encodedKey = OsSecretVault.GetSecret(VaultService, VaultAccount);
                if (!string.IsNullOrWhiteSpace(encodedKey))
                {
                    var existingKey = Convert.FromBase64String(encodedKey);
                    if (existingKey.Length == 32)
                    {
                        return existingKey;
                    }

                    CryptographicOperations.ZeroMemory(existingKey);
                    throw new InvalidOperationException("The key stored in the operating-system vault has an invalid length.");
                }

                if (!createIfMissing)
                {
                    throw new CryptographicException("The operating-system vault key for this database is missing.");
                }

                var newKey = RandomNumberGenerator.GetBytes(32);
                OsSecretVault.SetSecret(VaultService, VaultAccount, Convert.ToBase64String(newKey));
                return newKey;
            }
            catch (CryptographicException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Stanza cannot access the operating-system secure storage. Secrets were not read or saved.", exception);
            }
        }
    }
}
