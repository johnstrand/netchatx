using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Stanza.Storage.Security;

internal static class SecretProtection
{
    private const string TextPrefix = "stz:enc:v1:";

    public static bool IsProtectedText(string value)
    {
        if (!value.StartsWith(TextPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return AesGcmSecretProtector.IsProtected(Convert.FromBase64String(value[TextPrefix.Length..]));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsProtectedBytes(byte[] value)
        => AesGcmSecretProtector.IsProtected(value);

    public static string ProtectText(string value, ISecretProtector protector)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return TextPrefix + Convert.ToBase64String(protector.Protect(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string UnprotectText(string value, ISecretProtector protector)
    {
        if (!IsProtectedText(value))
        {
            throw new CryptographicException("A plaintext secret was found after the protected-secret database migration.");
        }

        var protectedBytes = Convert.FromBase64String(value[TextPrefix.Length..]);
        var plaintext = protector.Unprotect(protectedBytes);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static byte[] ProtectBytes(byte[] value, ISecretProtector protector)
        => protector.Protect(value);

    public static byte[] UnprotectBytes(byte[] value, ISecretProtector protector)
    {
        if (!IsProtectedBytes(value))
        {
            throw new CryptographicException("A plaintext secret was found after the protected-secret database migration.");
        }

        return protector.Unprotect(value);
    }

    public static void ValidateExistingSecrets(SqliteConnection connection, ISecretProtector protector)
    {
        ValidateTextColumn(connection, protector, "accounts", "password");
        ValidateTextColumn(connection, protector, "omemo_identities", "identity_key_private");
        ValidateTextColumn(connection, protector, "omemo_prekeys", "private_key");
        ValidateTextColumn(connection, protector, "omemo_signed_prekeys", "private_key");

        using var sessionCommand = connection.CreateCommand();
        sessionCommand.CommandText = "SELECT session_data FROM omemo_sessions LIMIT 1;";
        if (sessionCommand.ExecuteScalar() is byte[] sessionData)
        {
            var plaintext = UnprotectBytes(sessionData, protector);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void ValidateTextColumn(SqliteConnection connection, ISecretProtector protector, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} LIMIT 1;";
        if (command.ExecuteScalar() is not string protectedText)
        {
            return;
        }

        if (!IsProtectedText(protectedText))
        {
            throw new CryptographicException($"Plaintext secret material remains in {table}.{column} after migration.");
        }

        var protectedBytes = Convert.FromBase64String(protectedText[TextPrefix.Length..]);
        var plaintext = protector.Unprotect(protectedBytes);
        CryptographicOperations.ZeroMemory(plaintext);
    }
}
