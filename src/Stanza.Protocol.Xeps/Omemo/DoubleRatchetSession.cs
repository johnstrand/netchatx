using System.Security.Cryptography;
using System.Text;

namespace Stanza.Protocol.Xeps.Omemo;

public sealed class DoubleRatchetSession
{
    private static readonly byte[] RootKdfInfo = "StanzaOmemoRoot"u8.ToArray();
    private static readonly byte[] ChainKdfInfo = "StanzaOmemoChain"u8.ToArray();

    private byte[] _rootKey;
    private byte[]? _sendingChainKey;
    private byte[]? _receivingChainKey;

    public byte[] RootKey => _rootKey;
    public KeyPairData DHPair { get; private set; }
    public byte[]? RemoteDHPublicKey { get; private set; }

    public byte[]? SendingChainKey => _sendingChainKey;
    public byte[]? ReceivingChainKey => _receivingChainKey;

    public uint Ns { get; private set; } // Send message number
    public uint Nr { get; private set; } // Receive message number

    public DoubleRatchetSession(byte[] rootKey, KeyPairData? localDHPair = null, byte[]? remoteDHPublicKey = null, bool isInitiator = true)
    {
        _rootKey = (byte[])rootKey.Clone();
        DHPair = localDHPair ?? OmemoCrypto.GenerateX25519KeyPair();
        RemoteDHPublicKey = remoteDHPublicKey;

        if (isInitiator && remoteDHPublicKey is not null)
        {
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, remoteDHPublicKey);
            var (newRoot, sendChain) = KdfRk(_rootKey, dhSecret);
            ZeroAndReplace(ref _rootKey, newRoot);
            ZeroAndReplaceNullable(ref _sendingChainKey, sendChain);
        }
        else if (!isInitiator && remoteDHPublicKey is not null)
        {
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, remoteDHPublicKey);
            var (newRoot, recvChain) = KdfRk(_rootKey, dhSecret);
            ZeroAndReplace(ref _rootKey, newRoot);
            ZeroAndReplaceNullable(ref _receivingChainKey, recvChain);
        }
    }

    private static void ZeroAndReplace(ref byte[] target, byte[] newValue)
    {
        CryptographicOperations.ZeroMemory(target);
        target = newValue;
    }

    private static void ZeroAndReplaceNullable(ref byte[]? target, byte[]? newValue)
    {
        if (target is not null)
        {
            CryptographicOperations.ZeroMemory(target);
        }
        target = newValue;
    }

    public (byte[] Key, byte[] Iv, byte[] EphemeralPublicKey, uint MessageNumber) RatchetEncrypt()
    {
        if (SendingChainKey is null)
        {
            if (RemoteDHPublicKey is null)
                throw new InvalidOperationException("Cannot encrypt: no remote public key available.");

            DHPair = OmemoCrypto.GenerateX25519KeyPair();
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (newRoot, sendChain) = KdfRk(_rootKey, dhSecret);
            ZeroAndReplace(ref _rootKey, newRoot);
            ZeroAndReplaceNullable(ref _sendingChainKey, sendChain);
            Ns = 0;
        }

        var (nextChainKey, messageKey) = KdfCk(SendingChainKey!);
        ZeroAndReplaceNullable(ref _sendingChainKey, nextChainKey);

        var iv = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 12, null, "IV"u8.ToArray());
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 16, null, "KEY"u8.ToArray());

        CryptographicOperations.ZeroMemory(messageKey);

        var num = Ns++;
        return (key, iv, DHPair.PublicKey, num);
    }

    public (byte[] Key, byte[] Iv) RatchetDecrypt(byte[] remoteEphemeralPublicKey, uint messageNumber)
    {
        if (RemoteDHPublicKey is null || !CryptographicOperations.FixedTimeEquals(RemoteDHPublicKey, remoteEphemeralPublicKey))
        {
            RemoteDHPublicKey = remoteEphemeralPublicKey;
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (newRoot, recvChain) = KdfRk(_rootKey, dhSecret);
            ZeroAndReplace(ref _rootKey, newRoot);
            ZeroAndReplaceNullable(ref _receivingChainKey, recvChain);
            Nr = 0;

            // Prepare next DH pair for return transmissions
            DHPair = OmemoCrypto.GenerateX25519KeyPair();
            var sendDhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (sendRoot, sendChain) = KdfRk(_rootKey, sendDhSecret);
            ZeroAndReplace(ref _rootKey, sendRoot);
            ZeroAndReplaceNullable(ref _sendingChainKey, sendChain);
            Ns = 0;
        }

        if (ReceivingChainKey is null)
            throw new InvalidOperationException("Receiving chain key not initialized.");

        var (nextChainKey, messageKey) = KdfCk(ReceivingChainKey);
        ZeroAndReplaceNullable(ref _receivingChainKey, nextChainKey);
        Nr++;

        var iv = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 12, null, "IV"u8.ToArray());
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 16, null, "KEY"u8.ToArray());

        CryptographicOperations.ZeroMemory(messageKey);

        return (key, iv);
    }

    private static (byte[] RootKey, byte[] ChainKey) KdfRk(byte[] rk, byte[] dhOut)
    {
        var derived = OmemoCrypto.DeriveKey(dhOut, rk, RootKdfInfo, 64);
        var newRoot = new byte[32];
        var newChain = new byte[32];
        Buffer.BlockCopy(derived, 0, newRoot, 0, 32);
        Buffer.BlockCopy(derived, 32, newChain, 0, 32);
        CryptographicOperations.ZeroMemory(derived);
        CryptographicOperations.ZeroMemory(dhOut);
        return (newRoot, newChain);
    }

    private static (byte[] NextChainKey, byte[] MessageKey) KdfCk(byte[] ck)
    {
        var derived = OmemoCrypto.DeriveKey(ck, "StanzaChainSalt"u8.ToArray(), ChainKdfInfo, 64);
        var nextChain = new byte[32];
        var messageKey = new byte[32];
        Buffer.BlockCopy(derived, 0, nextChain, 0, 32);
        Buffer.BlockCopy(derived, 32, messageKey, 0, 32);
        CryptographicOperations.ZeroMemory(derived);
        return (nextChain, messageKey);
    }
}
