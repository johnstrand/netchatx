using System.Security.Cryptography;
using System.Text;

namespace NetChatx.Protocol.Xeps.Omemo;

public sealed class DoubleRatchetSession
{
    private static readonly byte[] RootKdfInfo = "NetChatxOmemoRoot"u8.ToArray();
    private static readonly byte[] ChainKdfInfo = "NetChatxOmemoChain"u8.ToArray();

    public byte[] RootKey { get; private set; }
    public KeyPairData DHPair { get; private set; }
    public byte[]? RemoteDHPublicKey { get; private set; }

    public byte[]? SendingChainKey { get; private set; }
    public byte[]? ReceivingChainKey { get; private set; }

    public uint Ns { get; private set; } // Send message number
    public uint Nr { get; private set; } // Receive message number

    public DoubleRatchetSession(byte[] rootKey, KeyPairData? localDHPair = null, byte[]? remoteDHPublicKey = null, bool isInitiator = true)
    {
        RootKey = rootKey;
        DHPair = localDHPair ?? OmemoCrypto.GenerateX25519KeyPair();
        RemoteDHPublicKey = remoteDHPublicKey;

        if (isInitiator && remoteDHPublicKey is not null)
        {
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, remoteDHPublicKey);
            var (newRoot, sendChain) = KdfRk(RootKey, dhSecret);
            RootKey = newRoot;
            SendingChainKey = sendChain;
        }
        else if (!isInitiator && remoteDHPublicKey is not null)
        {
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, remoteDHPublicKey);
            var (newRoot, recvChain) = KdfRk(RootKey, dhSecret);
            RootKey = newRoot;
            ReceivingChainKey = recvChain;
        }
    }

    public (byte[] Key, byte[] Iv, byte[] EphemeralPublicKey, uint MessageNumber) RatchetEncrypt()
    {
        if (SendingChainKey is null)
        {
            if (RemoteDHPublicKey is null)
                throw new InvalidOperationException("Cannot encrypt: no remote public key available.");

            DHPair = OmemoCrypto.GenerateX25519KeyPair();
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (newRoot, sendChain) = KdfRk(RootKey, dhSecret);
            RootKey = newRoot;
            SendingChainKey = sendChain;
            Ns = 0;
        }

        var (nextChainKey, messageKey) = KdfCk(SendingChainKey);
        SendingChainKey = nextChainKey;

        byte[] iv = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 12, null, "IV"u8.ToArray());
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 16, null, "KEY"u8.ToArray());

        uint num = Ns++;
        return (key, iv, DHPair.PublicKey, num);
    }

    public (byte[] Key, byte[] Iv) RatchetDecrypt(byte[] remoteEphemeralPublicKey, uint messageNumber)
    {
        if (RemoteDHPublicKey is null || !CryptographicOperations.FixedTimeEquals(RemoteDHPublicKey, remoteEphemeralPublicKey))
        {
            RemoteDHPublicKey = remoteEphemeralPublicKey;
            var dhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (newRoot, recvChain) = KdfRk(RootKey, dhSecret);
            RootKey = newRoot;
            ReceivingChainKey = recvChain;
            Nr = 0;

            // Prepare next DH pair for return transmissions
            DHPair = OmemoCrypto.GenerateX25519KeyPair();
            var sendDhSecret = OmemoCrypto.CalculateSharedSecret(DHPair.PrivateKey, RemoteDHPublicKey);
            var (sendRoot, sendChain) = KdfRk(RootKey, sendDhSecret);
            RootKey = sendRoot;
            SendingChainKey = sendChain;
            Ns = 0;
        }

        if (ReceivingChainKey is null)
            throw new InvalidOperationException("Receiving chain key not initialized.");

        var (nextChainKey, messageKey) = KdfCk(ReceivingChainKey);
        ReceivingChainKey = nextChainKey;
        Nr++;

        byte[] iv = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 12, null, "IV"u8.ToArray());
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, messageKey, 16, null, "KEY"u8.ToArray());

        return (key, iv);
    }

    private static (byte[] RootKey, byte[] ChainKey) KdfRk(byte[] rk, byte[] dhOut)
    {
        byte[] derived = OmemoCrypto.DeriveKey(dhOut, rk, RootKdfInfo, 64);
        byte[] newRoot = new byte[32];
        byte[] newChain = new byte[32];
        Buffer.BlockCopy(derived, 0, newRoot, 0, 32);
        Buffer.BlockCopy(derived, 32, newChain, 0, 32);
        return (newRoot, newChain);
    }

    private static (byte[] NextChainKey, byte[] MessageKey) KdfCk(byte[] ck)
    {
        byte[] derived = OmemoCrypto.DeriveKey(ck, "NetChatxChainSalt"u8.ToArray(), ChainKdfInfo, 64);
        byte[] nextChain = new byte[32];
        byte[] messageKey = new byte[32];
        Buffer.BlockCopy(derived, 0, nextChain, 0, 32);
        Buffer.BlockCopy(derived, 32, messageKey, 0, 32);
        return (nextChain, messageKey);
    }
}
