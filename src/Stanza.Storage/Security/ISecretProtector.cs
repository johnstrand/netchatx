namespace Stanza.Storage.Security;

/// <summary>
/// Protects sensitive values before they are persisted and restores them when read.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedValue);
}
