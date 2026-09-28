using System.Security.Cryptography;
using Stanza.Storage.Security;

namespace Stanza.Gui.Tests;

internal static class TestSecretProtector
{
    public static ISecretProtector Instance { get; } = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
}
